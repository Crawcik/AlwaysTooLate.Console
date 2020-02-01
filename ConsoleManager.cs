﻿// AlwaysTooLate.Console (c) 2018-2019 Always Too Late.

using System;
using System.Collections.Generic;
using System.Linq;
using AlwaysTooLate.Commands;
using AlwaysTooLate.Core;
using AlwaysTooLate.CVars;
using UnityEngine;

namespace AlwaysTooLate.Console
{
    /// <summary>
    ///     ConsoleManager class. Implements console behavior.
    ///     Should be initialized on main (entry) scene.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [RequireComponent(typeof(CommandManager))] // Commands are mandatory
    public class ConsoleManager : BehaviourSingleton<ConsoleManager>
    {
        public const string ConsolePrefabName = "AlwaysTooLate.Console";
        private readonly List<string> _highlights = new List<string>(32);

        private readonly List<string> _previousCommands = new List<string>(32);

        private int _commandCursor;
        private GameObject _console;
        private ConsoleHandler _handler;

        /// <summary>
        ///     The maximal number of messages that can be shown in the console.
        ///     When console message count exceeds this number, the oldest logs
        ///     will be removed.
        /// </summary>
        [Tooltip("The maximal number of messages that can be shown in the console. " +
                 "When console message count exceeds this number, " +
                 "the oldest logs will be removed.")]
        public int MaxMessages = 256;

        /// <summary>
        ///     The key that opens the console window.
        /// </summary>
        [Tooltip("The key that opens the console window.")]
        public KeyCode OpenConsoleKey = KeyCode.BackQuote;

        public bool IsSelectingHighlight => _commandCursor >= _previousCommands.Count;
        public bool IsConsoleOpen { get; private set; }

        protected override void OnAwake()
        {
            // Load and spawn Console canvas
            var consolePrefab = Resources.Load<GameObject>(ConsolePrefabName);
            Debug.Assert(consolePrefab, $"Console prefab ({ConsolePrefabName}) not found!");
            Debug.Assert(CommandManager.Instance,
                "CommandManager instance is missing! CommandManager is required for console to work, please add it somewhere.");
            _console = Instantiate(consolePrefab);
            _handler = _console.GetComponentInChildren<ConsoleHandler>();

            // Do not destroy the console game object on scene change
            DontDestroyOnLoad(_console);

            // Listen for logs (strip and add to the console)
            Application.logMessageReceived += OnLog;

            CommandManager.RegisterCommand("clear", "Clears the console", _handler.ClearConsole);
        }

        protected void Update()
        {
            if (Input.GetKeyDown(OpenConsoleKey)) SetConsoleActive(!IsConsoleOpen);

            if (IsConsoleOpen && _handler.IsEnteringCommand)
            {
                // Get previous/next command through Up/Down arrows
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    _commandCursor--;
                    UpdateHighlights();
                }
                else if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    _commandCursor++;
                    UpdateHighlights();
                }

                // Autocomplete
                if (Input.GetKeyDown(KeyCode.Tab))
                    // Try to complete the current command
                    Autocomplete();
            }
        }

        private void UpdateHighlights()
        {
            _commandCursor = Mathf.Clamp(_commandCursor, 0, _previousCommands.Count - 1 + _highlights.Count);

            if (IsSelectingHighlight)
                _handler.SelectHighlight(_commandCursor - _previousCommands.Count);
            else
                // Select from history
                _handler.CurrentCommand = _previousCommands[_commandCursor];
        }

        private void OnLog(string condition, string stacktrace, LogType type)
        {
            Color color;
            switch (type)
            {
                case LogType.Error:
                    color = Color.red;
                    break;
                case LogType.Assert:
                    color = Color.red;
                    break;
                case LogType.Warning:
                    color = Color.yellow;
                    break;
                case LogType.Log:
                    color = Color.white;
                    break;
                case LogType.Exception:
                    color = Color.magenta;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            _handler.AddLine(condition, color);
        }

        /// <summary>
        ///     Writes given text to the console window.
        /// </summary>
        /// <param name="text">The text.</param>
        public void Write(string text)
        {
            _handler.AddLine(text);
        }

        /// <summary>
        ///     Writes given text to the console window.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="color">The text color.</param>
        public void Write(string text, Color color)
        {
            _handler.AddLine(text, color);
        }

        public void Autocomplete()
        {
            if (IsSelectingHighlight)
            {
                var idx = _commandCursor - _previousCommands.Count;

                if (idx < _highlights.Count)
                {
                    _handler.CurrentCommand = _highlights[idx];
                    _handler.MoveCaretToEnd();
                }
            }
            else
            {
                if (_handler.CurrentCommand.Length == 0)
                    return;

                var currentCommand = _handler.CurrentCommand;
                var commands = CommandManager.GetCommands();

                foreach (var command in commands)
                    if (command.Name.StartsWith(currentCommand))
                    {
                        _handler.CurrentCommand = command.Name + " ";
                        break;
                    }

                _handler.MoveCaretToEnd();
            }
        }

        public void OnCommandUpdate(string command)
        {
            _highlights.Clear();

            if (command.Length == 0)
                return;

            var commands = CommandManager.GetCommands();
            foreach (var cmd in commands)
            {
                if (!cmd.Name.Contains(command))
                    continue;

                _highlights.Add(cmd.Name);

                if (_highlights.Count == 32)
                    break;
            }

            if (CVarManager.Instance != null)
            {
                var variables = CVarManager.Instance.AllVariables;

                foreach (var var in variables)
                {
                    if (!var.Key.Contains(command))
                        continue;

                    _highlights.Add(var.Key);

                    if (_highlights.Count == 32)
                        break;
                }
            }

            _highlights.Sort();

            _handler.SetHighlights(_highlights.ToArray(), command);
        }

        public void OnCommandEnter(string command)
        {
            if (command.Length == 0) // Don't even try...
                return;

            Debug.Log($"> {command}");
            CommandManager.Execute(command);
            _handler.ClearCommand();
            _handler.SetFocus();

            // Add command if not the same
            if (_previousCommands.Count == 0 || _previousCommands.Last() != command)
                _previousCommands.Add(command);

            // Set command selector cursor
            _commandCursor = _previousCommands.Count;

            // Remove commands that are old
            if (_previousCommands.Count >= 32)
                _previousCommands.RemoveAt(0);

            _handler.SetHighlights(null);
            _highlights.Clear();
        }

        /// <summary>
        ///     Sets console active state.
        /// </summary>
        public void SetConsoleActive(bool activeState)
        {
            IsConsoleOpen = activeState;

            if (IsConsoleOpen)
                _handler.Open();
            else
                _handler.Close();
        }
    }
}