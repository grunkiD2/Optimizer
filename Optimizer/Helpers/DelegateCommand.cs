using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Optimizer.Contracts.Services;

namespace Optimizer.Helpers
{
    /// <summary>
    /// A generic command implementation that encapsulates delegate actions.
    /// Implements both ICommand and ICommandService interfaces for WPF MVVM compatibility.
    /// Thread-safe with proper locking for multi-threaded environments.
    /// </summary>
    public class DelegateCommand<T> : ICommand, ICommandService
        where T : class
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool> _canExecute;
        private bool _isCanExecuteChanged = true;
        private object _lockObject = new object();

        /// <summary>
        /// Occurs when the ability of the command to execute has changed.
        /// </summary>
        public event EventHandler CanExecuteChanged;

        /// <summary>
        /// Initializes a new instance of the DelegateCommand&lt;T&gt; class.
        /// </summary>
        /// <param name="execute">The action to execute when Invoke/Execute is called.</param>
        public DelegateCommand(Action<T?> execute) : this(execute, null) { }

        /// <summary>
        /// Initializes a new instance of the DelegateCommand&lt;T&gt; class with optional canExecute predicate.
        /// </summary>
        /// <param name="execute">The action to execute when Invoke/Execute is called.</param>
        /// <param name="canExecute">Optional function used to determine if command execution is permitted.</param>
        public DelegateCommand(Action<T?> execute, Func<T?, bool> canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Raises the CanExecuteChanged event to notify clients that command availability may have changed.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            var handler = CanExecuteChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Gets a value indicating whether this command currently can execute.
        /// </summary>
        public bool IsCanExecuteChanged
        {
            get => _isCanExecuteChanged;
            set
            {
                if (_isCanExecuteChanged == value) return;

                lock (_lockObject)
                {
                    _isCanExecuteChanged = value;
                    RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Executes the command with the specified parameter.
        /// </summary>
        /// <param name="parameter">The data to pass to the execute action.</param>
        public void Execute(object parameter)
        {
            lock (_lockObject)
            {
                _execute((T?)parameter);
            }
        }

        /// <summary>
        /// Determines whether this command can be executed with the given parameter.
        /// </summary>
        /// <param name="parameter">The data to evaluate for command execution eligibility.</param>
        public bool CanExecute(object parameter)
        {
            lock (_lockObject)
            {
                return _canExecute?.Invoke((T?)parameter) ?? true;
            }
        }

        /// <summary>
        /// Executes the command with the specified parameter.
        /// </summary>
        /// <param name="parameter">The data to pass to the execute action.</param>
        public void Invoke(T? parameter)
        {
            Execute(parameter);
        }
    }

    /// <summary>
    /// A generic command implementation that encapsulates delegate actions.
    /// Implements both ICommand and ICommandService interfaces for WPF MVVM compatibility.
    /// Thread-safe with proper locking for multi-threaded environments.
    /// </summary>
    public class DelegateCommand : DelegateCommand<object>
    {
        /// <summary>
        /// Initializes a new instance of the DelegateCommand class.
        /// </summary>
        /// <param name="execute">The action to execute when Invoke/Execute is called.</param>
        public DelegateCommand(Action<object?> execute) : this(execute, null) { }

        /// <summary>
        /// Initializes a new instance of the DelegateCommand class with optional canExecute predicate.
        /// </summary>
        /// <param name="execute">The action to execute when Invoke/Execute is called.</param>
        /// <param name="canExecute">Optional function used to determine if command execution is permitted.</param>
        public DelegateCommand(Action<object?> execute, Func<object?, bool> canExecute) : base(execute, canExecute) { }
    }
}
