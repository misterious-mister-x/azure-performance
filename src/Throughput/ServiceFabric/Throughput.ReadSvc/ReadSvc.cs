﻿using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Serilog;
using Azure.Performance.Common;
using Azure.Performance.Throughput.Common;
using System.IO;

namespace Azure.Performance.Throughput.ReadSvc
{
	/// <summary>
	/// An instance of this class is created for each service replica by the Service Fabric runtime.
	/// </summary>
	internal sealed class ReadSvc : LoggingStatefulService, IPerformanceSvc
	{
		private const int TaskCount = 256;
		private const long KeyCount = 1024 * 1024;
		private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);
		private long _id = 0;

		public ReadSvc(StatefulServiceContext context, ILogger logger)
			: base(context, logger)
		{ }

		/// <summary>
		/// Optional override to create listeners (e.g., HTTP, Service Remoting, WCF, etc.) for this service replica to handle client or user requests.
		/// </summary>
		/// <remarks>
		/// For more information on service communication, see https://aka.ms/servicefabricservicecommunication
		/// </remarks>
		/// <returns>A collection of listeners.</returns>
		protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
		{
			return new[]
			{
				new ServiceReplicaListener(context => this.CreateServiceRemotingListener(context), "ServiceEndpoint"),
			};
		}

		public Task<HttpStatusCode> GetHealthAsync()
		{
			return Task.FromResult(HttpStatusCode.OK);
		}

		/// <summary>
		/// This is the main entry point for your service replica.
		/// This method executes when this replica of your service becomes primary and has write status.
		/// </summary>
		/// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service replica.</param>
		protected override async Task RunAsync(CancellationToken cancellationToken)
		{
			await base.RunAsync(cancellationToken).ConfigureAwait(false);

			// Spawn workload.
			await CreateWorkloadAsync(cancellationToken).ConfigureAwait(false);
		}

		private async Task CreateWorkloadAsync(CancellationToken cancellationToken)
		{
			var state = await this.StateManager.GetOrAddAsync<IReliableDictionary<long, PerformanceData>>("throughput").ConfigureAwait(false);

			await PopulateAsync(state, cancellationToken).ConfigureAwait(false);

			var workload = new ThroughputWorkload(_logger, "ReadReliableDictionary", IsKnownException);
			await workload.InvokeAsync(TaskCount, (random) => ReadAsync(state, random, cancellationToken), cancellationToken).ConfigureAwait(false);
		}

		private async Task<long> ReadAsync(IReliableDictionary<long, PerformanceData> state, Random random, CancellationToken cancellationToken)
		{
			using (var tx = StateManager.CreateTransaction())
			{
				long key = Interlocked.Increment(ref _id) % KeyCount;
				var result = await state.TryGetValueAsync(tx, key, DefaultTimeout, cancellationToken).ConfigureAwait(false);
				await tx.CommitAsync().ConfigureAwait(false);

				if (!result.HasValue)
					throw new InvalidDataException($"IReliableDictionary is missing key '{key}'.");
			}

			return 1;
		}

		/// <summary>
		/// Generate <see cref="KeyCount"/> keys using a number of concurrent tasks.
		/// </summary>
		private async Task PopulateAsync(IReliableDictionary<long, PerformanceData> state, CancellationToken cancellationToken)
		{
			try
			{
				long nextKey = -1;

				int taskCount = 32;
				var tasks = new List<Task>(taskCount);
				for (int i = 0; i < taskCount; i++)
				{
					int taskId = i;
					tasks.Add(Task.Run(async () =>
					{
						long key = 0;
						while (!cancellationToken.IsCancellationRequested && ((key = Interlocked.Increment(ref nextKey)) < KeyCount))
						{
							using (var tx = StateManager.CreateTransaction())
							{
								await state.GetOrAddAsync(tx, key, RandomGenerator.GetPerformanceData(), DefaultTimeout, cancellationToken).ConfigureAwait(false);
								await tx.CommitAsync();
							}
						}
					}));
				}

				await Task.WhenAll(tasks).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				_logger.Error(e, "Unexpected exception {ExceptionType} in {WorkloadName}.", e.GetType(), "PopulateAsync");
				throw;
			}
		}

		private static TimeSpan? IsKnownException(Exception e)
		{
			if (e is FabricNotPrimaryException || e is FabricNotReadableException)
				return TimeSpan.FromSeconds(1);

			return null;
		}
	}
}