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

namespace Azure.Performance.Throughput.DictionarySvc
{
	/// <summary>
	/// An instance of this class is created for each service replica by the Service Fabric runtime.
	/// </summary>
	internal sealed class DictionarySvc : LoggingStatefulService, IDictionarySvc
	{
		private const int TaskCount = 256;
		private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);
		private long _id = 0;

		public DictionarySvc(StatefulServiceContext context, ILogger logger)
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

			// Spawn worker tasks.
			await CreateWorkloadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private async Task CreateWorkloadAsync(CancellationToken cancellationToken)
		{
			var state = await this.StateManager.GetOrAddAsync<IReliableDictionary<long, PerformanceData>>("throughput").ConfigureAwait(false);

			var workload = new ThroughputWorkload(_logger, "ReliableDictionary");
			await workload.InvokeAsync(TaskCount, (random) => WriteAsync(state, random, cancellationToken), cancellationToken).ConfigureAwait(false);
		}

		private async Task<long> WriteAsync(IReliableDictionary<long, PerformanceData> state, Random random, CancellationToken cancellationToken)
		{
			const int batchSize = 8;

			using (var tx = StateManager.CreateTransaction())
			{
				for (int i = 0; i < batchSize; i++)
				{
					long key = Interlocked.Increment(ref _id) % 1000000;
					var value = RandomGenerator.GetPerformanceData();

					await state.SetAsync(tx, key, value, DefaultTimeout, cancellationToken).ConfigureAwait(false);
				}

				await tx.CommitAsync().ConfigureAwait(false);
			}

			return batchSize;
		}
	}
}
