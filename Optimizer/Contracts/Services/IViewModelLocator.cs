using System;

namespace Optimizer.Contracts.Services
{
    /// <summary>
    /// Defines the interface for view model factory pattern.
    /// Resolves view models by type name or interface using dependency injection container.
    /// </summary>
    public interface IViewModelLocator
    {
        /// <summary>
        /// Gets a view model instance of the specified type by calling GetService<T>() on App._host.Services.
        /// Uses DI container to resolve view models efficiently.
        /// </summary>
        /// <typeparam name="T">The specific ViewModel type to retrieve.</typeparam>
        /// <returns>The view model instance, or null if not registered in DI container.</returns>
        T GetViewModel<T>() where T : class;

        /// <summary>
        /// Creates a new view model instance using the application's generic host service provider.
        /// </summary>
        /// <typeparam name="T">The ViewModel type to instantiate.</typeparam>
        /// <returns>A newly created ViewModel instance of type T.</returns>
        T CreateViewModel<T>() where T : class;

        /// <summary>
        /// Gets or caches a view model if it has been previously retrieved.
        /// </summary>
        /// <param name="viewModel">The cached ViewModel to retrieve.</param>
        /// <typeparam name="T">The expected ViewModel type.</typeparam>
        /// <returns>The cached ViewModel instance, or null if not found.</returns>
        T GetCachedViewModel<T>(out object viewModel);
    }
}
