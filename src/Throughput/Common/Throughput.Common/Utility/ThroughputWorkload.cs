﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Azure.Performance.Common;

namespace Azure.Performance.Throughput.Common
{
	public sealed class ThroughputWorkload
	{
		private readonly ILogger _logger;
		private readonly string _workloadName;
		private readonly Func<Exception, TimeSpan?> _throttle;

		private long _operations = 0;
		private long _latency = 0;

		public ThroughputWorkload(ILogger logger, string workloadName, Func<Exception, TimeSpan?> throttle = null)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_workloadName = workloadName ?? throw new ArgumentNullException(nameof(workloadName));
			_throttle = throttle ?? new Func<Exception, TimeSpan?>((e) => null);
		}

		public async Task InvokeAsync(int taskCount, Func<Random, Task<long>> workload, CancellationToken cancellationToken)
		{
			// Spawn the workload workers.
			var tasks = new List<Task>(taskCount + 1);
			for (int i = 0; i < taskCount; i++)
			{
				int taskId = i;
				tasks.Add(Task.Run(() => CreateWorkerAsync(workload, taskId, cancellationToken)));
			}

			// Spawn the metric tracker.
			var metrics = new Thread(() => TrackMetrics(cancellationToken)) { Priority = ThreadPriority.AboveNormal };
			metrics.Start();

			// Run until cancelled.
			await Task.WhenAll(tasks).ConfigureAwait(false);
		}

		private void TrackMetrics(CancellationToken cancellationToken)
		{
			var timer = new Stopwatch();
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					timer.Restart();
					Thread.Sleep(TimeSpan.FromSeconds(1));
					timer.Stop();

					// Read the latest metrics.
					long operations = Interlocked.Read(ref _operations);
					long latency = Interlocked.Read(ref _latency);
					if (operations == 0)
						continue;

					// Log metrics - operations/sec and latency/operation.
					double throughput = ((double)operations * 1000) / Math.Max((double)timer.ElapsedMilliseconds, 1000);
					double latencyPerOperation = (double)latency / (double)operations;
					_logger.Information("Throughput statistics: {WorkloadName} {Operations} {Throughput} {OperationLatency} {ElapsedTimeInMs}",
						_workloadName, operations, throughput, latencyPerOperation, timer.ElapsedMilliseconds);

					// Subtract out the metrics that were logged.
					Interlocked.Add(ref _operations, -operations);
					Interlocked.Add(ref _latency, -latency);
				}
				catch (Exception e)
				{
					_logger.Error(e, "Unexpected exception {ExceptionType} in {WorkloadName} tracking metrics.", e.GetType(), _workloadName);
				}
			}
		}

		private async Task CreateWorkerAsync(Func<Random, Task<long>> workload, int taskId, CancellationToken cancellationToken)
		{
			var timer = new Stopwatch();
			var retry = new RetryHandler();
			var random = new Random();

			while (!cancellationToken.IsCancellationRequested)
			{
				timer.Restart();
				try
				{
					// Invoke the workload.
					long operations = await workload.Invoke(random).ConfigureAwait(false);
					timer.Stop();
					retry.Reset();

					// Track metrics.
					Interlocked.Add(ref _operations, operations);
					Interlocked.Add(ref _latency, timer.ElapsedMilliseconds);
				}
				catch (Exception e)
				{
					timer.Stop();

					if (cancellationToken.IsCancellationRequested)
						return;

					// Check if this is an exception indicating we are being throttled.
					var throttle = _throttle.Invoke(e);
					if (throttle != null)
					{
						await Task.Delay(throttle.Value, cancellationToken).ConfigureAwait(false);
					}
					else
					{
						var retryAfter = retry.Retry();

						// Track metrics.
						Interlocked.Add(ref _latency, timer.ElapsedMilliseconds);

						// Exponential delay after exceptions.
						_logger.Error(e, "Unexpected exception {ExceptionType} in {WorkloadName}.  Retrying in {RetryInMs} ms.", e.GetType(), _workloadName, retryAfter.TotalMilliseconds);
						await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);
					}
				}
			}
		}
	}
}
