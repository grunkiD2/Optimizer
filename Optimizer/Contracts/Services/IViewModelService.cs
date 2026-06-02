using System;

namespace Optimizer.Contracts.Services
{
    /// <summary>
    /// Defines the interface for view model lifecycle management.
    /// Provides Initialize and Dispose methods for proper service integration.
    /// </summary>
    public interface IViewModelService
    {
        /// <summary>
        /// Initializes the view model with necessary dependencies and state.
        /// Called when a ViewModel is first instantiated or navigated to.
        /// </summary>
        /// <param name="viewModel">The view model instance to initialize.</param>
        void Initialize(object viewModel);

        /// <summary>
        /// Performs cleanup operations for the view model.
        /// Called when the ViewModel is being disposed or no longer needed.
        /// </summary>
        /// <param name="viewModel">The view model instance to dispose of.</param>
        void Dispose(object viewModel);

        /// <summary>
        /// Creates a new view model instance by type name if not already created.
        /// Uses factory pattern for efficient view model instantiation.
        /// </summary>
        /// <typeparam name="T">The specific ViewModel type to create.</typeparam>
        /// <returns>The newly created or existing view model instance.</returns>
        T GetOrCreateViewModel<T>() where T : class;

        /// <summary>
        /// Gets the currently active view model if one exists.
        /// </summary>
        /// <returns>The current ViewModel instance, or null if none is set.</returns>
        object GetCurrentViewModel();
    }
}
