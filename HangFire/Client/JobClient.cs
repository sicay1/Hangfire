﻿using System;
using System.Collections.Generic;
using HangFire.States;
using ServiceStack.Redis;

namespace HangFire.Client
{
    internal class JobClient : IDisposable
    {
        private readonly ClientJobInvoker _jobInvoker = ClientJobInvoker.Current;
        private readonly IRedisClient _redis = RedisFactory.Create();

        public string Async(Type jobType, object args = null)
        {
            if (jobType == null)
            {
                throw new ArgumentNullException("jobType");
            }
            if (!typeof (BackgroundJob).IsAssignableFrom(jobType))
            {
                throw new ArgumentException(
                    String.Format("The type '{0}' must inherit '{1}'.", jobType, typeof(BackgroundJob)), 
                    "jobType");
            }

            var queueName = JobHelper.GetQueueName(jobType);

            var clientContext = new ClientContext();
            var descriptor = CreateDescriptor(jobType, args);

            descriptor.EnqueueAction = () =>
                {
                    lock (_redis)
                    {
                        CreateJob(descriptor.JobId, descriptor.Job);
                        JobState.Apply(
                            _redis, 
                            new EnqueuedState(descriptor.JobId, "Enqueued by the Сlient", queueName));
                    }
                };

            _jobInvoker.EnqueueJob(clientContext, descriptor);

            return descriptor.JobId;
        }

        public string In(TimeSpan interval, Type jobType, object args = null)
        {
            if (jobType == null)
            {
                throw new ArgumentNullException("jobType");
            }
            if (!typeof(BackgroundJob).IsAssignableFrom(jobType))
            {
                throw new ArgumentException(
                    String.Format("The type '{0}' must inherit '{1}'.", jobType, typeof(BackgroundJob)),
                    "jobType");
            }

            if (interval != interval.Duration())
            {
                throw new ArgumentOutOfRangeException("interval", "Interval value can not be negative.");
            }

            if (interval.Equals(TimeSpan.Zero))
            {
                return Async(jobType, args);
            }

            var clientContext = new ClientContext();
            var descriptor = CreateDescriptor(jobType, args);

            var at = DateTime.UtcNow.Add(interval);
            var queueName = JobHelper.GetQueueName(jobType);

            descriptor.EnqueueAction = () =>
            {
                lock (_redis)
                {
                    CreateJob(descriptor.JobId, descriptor.Job);

                    JobState.Apply(_redis, new ScheduledState(
                        descriptor.JobId, 
                        "Scheduled by the Client",
                        queueName, 
                        at));
                }
            };

            _jobInvoker.EnqueueJob(clientContext, descriptor);

            return descriptor.JobId;
        }

        public void Dispose()
        {
            _redis.Dispose();
        }

        private ClientJobDescriptor CreateDescriptor(Type jobType, object jobArgs)
        {
            var job = new Dictionary<string, string>();
            var descriptor = new ClientJobDescriptor(GenerateId(), job);

            job["Type"] = jobType.AssemblyQualifiedName;
            job["Args"] = JobHelper.ToJson(descriptor.SerializeProperties(jobArgs));

            return descriptor;
        }

        private void CreateJob(string id, Dictionary<string, string> properties)
        {
            _redis.SetRangeInHash(
                String.Format("hangfire:job:{0}", id),
                properties);
        }

        private string GenerateId()
        {
            return Guid.NewGuid().ToString();
        }
    }
}
