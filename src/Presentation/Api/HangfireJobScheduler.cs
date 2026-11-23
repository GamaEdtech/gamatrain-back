namespace GamaEdtech.Presentation.Api
{
    using System.Linq.Expressions;

    using GamaEdtech.Application.Interface;

    using Hangfire;

    /// <summary><see cref="IJobScheduler"/> on Hangfire, the API's job runner (its jobs are stored in the database).</summary>
    public sealed class HangfireJobScheduler(IBackgroundJobClient client) : IJobScheduler
    {
        public void Schedule<TService>(Expression<Func<TService, Task>> call, TimeSpan delay)
            where TService : notnull => _ = client.Schedule(call, delay);
    }
}
