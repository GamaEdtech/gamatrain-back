namespace GamaEdtech.Application.Interface
{
    using System.Linq.Expressions;

    /// <summary>
    /// Runs a service call later, off the request (Hangfire in the API, which registers it): the application's view of the
    /// job runner, so services schedule work without depending on it. The call is stored, so its arguments must be plain
    /// values (ids), never entities or the request's state. See docs/architecture/cross-cutting-concerns.md, "Background jobs".
    /// </summary>
    public interface IJobScheduler
    {
        /// <summary>Calls <paramref name="call"/> on a fresh <typeparamref name="TService"/> after <paramref name="delay"/>.</summary>
        void Schedule<TService>(Expression<Func<TService, Task>> call, TimeSpan delay)
            where TService : notnull;
    }
}
