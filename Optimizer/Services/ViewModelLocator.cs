using System;
using Optimizer.Contracts.Services;

namespace Optimizer.Services
{
    /// <summary>
    /// Implements factory pattern for resolving view models using dependency injection container.
    /// Delegates to App.GetService&lt;T&gt;() which wraps _host.Services.
    /// </summary>
    public class ViewModelLocator : IViewModelLocator
    {
        private readonly System.IServiceProvider _serviceProvider;

        public ViewModelLocator(System.IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Gets a view model instance of the specified type using DI container.
        /// </summary>
        /// <typeparam name="T">The ViewModel type to retrieve.</typeparam>
        /// <returns>The view model instance, or null if not registered.</returns>
        public T GetViewModel<T>() where T : class
        {
            return _serviceProvider.GetService(typeof(T)) as T;
        }

        /// <summary>
        /// Creates a new view model instance using the DI container.
        /// </summary>
        /// <typeparam name="T">The ViewModel type to instantiate.</typeparam>
        /// <returns>A newly created ViewModel instance of type T.</returns>
        public T CreateViewModel<T>() where T : class
        {
            return _serviceProvider.GetService(typeof(T)) as T;
        }

        /// <summary>
        /// Gets or caches a view model if previously retrieved.
        /// </summary>
        /// <param name="viewModel">The cached ViewModel to retrieve.</param>
        /// <typeparam name="T">The expected ViewModel type.</typeparam>
        /// <returns>The cached ViewModel instance, or null if not found.</returns>
        public T GetCachedViewModel<T>(out object viewModel)
        {
            viewModel = _serviceProvider.GetService(typeof(T));
            return (T)viewModel;
        }
    }
}
