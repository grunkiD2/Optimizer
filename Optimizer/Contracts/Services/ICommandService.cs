using System;

namespace Optimizer.Contracts.Services
{
    /// <summary>
    /// Defines the interface for implementing commands in WPF MVVM applications.
    /// Provides Execute and CanExecute functionality with proper event handling.
    /// </summary>
    public interface ICommandService
    {
        /// <summary>
        /// Occurs when changes occur that affect whether or not the command should execute. Subscribe to this event
        /// to enable automatic command execution whenever a property of the command's target object has changed.
        /// </summary>
        event EventHandler CanExecuteChanged;

        /// <summary>
        /// Executes the command with the specified parameter (if any).
        /// </summary>
        /// <param name="parameter">Data used by the command. If the command does not require data you can
        /// pass a null value</param>
        void Execute(object parameter);

        /// <summary>
        /// Determines whether this command can be executed with the given parameter.
        /// </summary>
        /// <param name="parameter">Data used by the command. If the command does not require data you can
        /// pass a null value</param>
        bool CanExecute(object parameter);

        /// <summary>
        /// Gets or sets a value indicating whether this command currently can execute.
        /// </summary>
        bool IsCanExecuteChanged { get; set; }
    }
}
