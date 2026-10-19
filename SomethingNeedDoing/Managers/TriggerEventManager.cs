using SomethingNeedDoing.Core.Events;
using SomethingNeedDoing.Core.Interfaces;
using System.Threading.Tasks;

namespace SomethingNeedDoing.Managers;

/// <summary>
/// Represents a function that can be triggered by an event.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TriggerFunction"/> class.
/// </remarks>
/// <param name="macro">The macro containing the function.</param>
/// <param name="functionName">The name of the function.</param>
/// <param name="eventType">The trigger event this function handles.</param>
public class TriggerFunction(IMacro macro, string functionName, TriggerEvent eventType)
{
    /// <summary>
    /// Gets the macro containing this function.
    /// </summary>
    public IMacro Macro { get; } = macro;

    /// <summary>
    /// Gets the name of the function.
    /// </summary>
    public string FunctionName { get; } = functionName;

    /// <summary>
    /// Gets the trigger event this function handles.
    /// </summary>
    public TriggerEvent EventType { get; } = eventType;

    /// <summary>
    /// Gets the addon name for OnAddonEvent triggers.
    /// </summary>
    public string? AddonName { get; } = eventType == TriggerEvent.OnAddonEvent && functionName.StartsWith("OnAddonEvent_")
        ? functionName.Split('_')[1]
        : null;

    /// <summary>
    /// Gets the event type for OnAddonEvent triggers.
    /// </summary>
    public string? AddonEventType { get; } = eventType == TriggerEvent.OnAddonEvent && functionName.StartsWith("OnAddonEvent_")
        ? functionName.Split('_')[2]
        : null;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is TriggerFunction other && Macro.Id == other.Macro.Id && FunctionName == other.FunctionName;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Macro.Id, FunctionName);
}

/// <summary>
/// Manages trigger events for macros.
/// </summary>
public class TriggerEventManager : IDisposable
{
    public Dictionary<TriggerEvent, List<TriggerFunction>> EventHandlers { get; } = [];

    /// <summary>
    /// Event raised when a trigger event occurs.
    /// </summary>
    public event EventHandler<TriggerEventArgs>? TriggerEventOccurred;

    /// <summary>
    /// Event raised when a function execution is requested.
    /// </summary>
    public event EventHandler<FunctionExecutionRequestedEventArgs>? FunctionExecutionRequested;

    /// <summary>
    /// Registers a macro to handle a specific trigger event.
    /// </summary>
    /// <param name="macro">The macro to register.</param>
    /// <param name="eventType">The type of event to register for.</param>
    public void RegisterTrigger(IMacro macro, TriggerEvent eventType)
    {
        if (!EventHandlers.ContainsKey(eventType))
            EventHandlers[eventType] = [];

        var triggerFunction = new TriggerFunction(macro, string.Empty, eventType);
        if (!EventHandlers[eventType].Contains(triggerFunction))
            EventHandlers[eventType].Add(triggerFunction);
    }

    /// <summary>
    /// Registers a function within a macro to handle a trigger event based on its name.
    /// </summary>
    /// <param name="macro">The macro containing the function.</param>
    /// <param name="functionName">The name of the function to register.</param>
    public void RegisterFunctionTrigger(IMacro macro, string functionName)
    {
        // Check for OnAddonEvent function name pattern
        if (functionName.StartsWith("OnAddonEvent_"))
        {
            var parts = functionName.Split('_');
            if (parts.Length == 3) // OnAddonEvent_AddonName_EventType
            {
                if (!EventHandlers.ContainsKey(TriggerEvent.OnAddonEvent))
                    EventHandlers[TriggerEvent.OnAddonEvent] = [];

                var triggerFunction = new TriggerFunction(macro, functionName, TriggerEvent.OnAddonEvent);
                if (EventHandlers[TriggerEvent.OnAddonEvent].Contains(triggerFunction))
                {
                    FrameworkLogger.Debug($"Function trigger already registered for macro {macro.Name} function {functionName} (Addon: {parts[1]}, Event: {parts[2]})");
                    return;
                }
                FrameworkLogger.Debug($"Registering OnAddonEvent trigger for macro {macro.Name} function {functionName} (Addon: {parts[1]}, Event: {parts[2]})");
                EventHandlers[TriggerEvent.OnAddonEvent].Add(triggerFunction);
                return;
            }
        }

        // Check if the function name starts with any trigger event name
        foreach (var eventType in Enum.GetValues<TriggerEvent>())
        {
            if (eventType == TriggerEvent.None) continue;

            if (functionName.StartsWith(eventType.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (!EventHandlers.ContainsKey(eventType))
                    EventHandlers[eventType] = [];

                var triggerFunction = new TriggerFunction(macro, functionName, eventType);
                if (EventHandlers[eventType].Contains(triggerFunction))
                {
                    FrameworkLogger.Debug($"Function trigger already registered for macro {macro.Name} function {functionName} event {eventType}");
                    return;
                }
                FrameworkLogger.Debug($"Registering trigger event {eventType} for macro {macro.Name} function {functionName}");
                EventHandlers[eventType].Add(triggerFunction);
                return;
            }
        }
    }

    /// <summary>
    /// Unregisters a macro from handling a specific trigger event.
    /// </summary>
    /// <param name="macro">The macro to unregister.</param>
    /// <param name="eventType">The type of event to unregister from.</param>
    public void UnregisterTrigger(IMacro macro, TriggerEvent eventType)
    {
        if (EventHandlers.TryGetValue(eventType, out var value))
            value.RemoveAll(tf => tf.Macro.Id == macro.Id && string.IsNullOrEmpty(tf.FunctionName));
    }

    /// <summary>
    /// Unregisters a function within a macro from handling its trigger event.
    /// </summary>
    /// <param name="macro">The macro containing the function.</param>
    /// <param name="functionName">The name of the function to unregister.</param>
    public void UnregisterFunctionTrigger(IMacro macro, string functionName)
    {
        // Check for OnAddonEvent function name pattern
        if (functionName.StartsWith("OnAddonEvent_"))
        {
            var parts = functionName.Split('_');
            if (parts.Length == 3) // OnAddonEvent_AddonName_EventType
            {
                if (EventHandlers.TryGetValue(TriggerEvent.OnAddonEvent, out var value))
                {
                    var removed = value.RemoveAll(tf => tf.Macro.Id == macro.Id && tf.FunctionName == functionName);
                    if (removed > 0)
                        FrameworkLogger.Debug($"Unregistering OnAddonEvent trigger for macro {macro.Name} function {functionName}");
                }
                return;
            }
        }

        // Check if the function name starts with any trigger event name
        foreach (var eventType in Enum.GetValues<TriggerEvent>())
        {
            if (eventType == TriggerEvent.None) continue;

            if (functionName.StartsWith(eventType.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (EventHandlers.TryGetValue(eventType, out var value))
                {
                    var removed = value.RemoveAll(tf => tf.Macro.Id == macro.Id && tf.FunctionName == functionName);
                    if (removed > 0)
                        FrameworkLogger.Debug($"Unregistering trigger event {eventType} for macro {macro.Name} function {functionName}");
                }
                return;
            }
        }
    }

    /// <summary>
    /// Unregisters a macro from all trigger events.
    /// </summary>
    /// <param name="macro">The macro to unregister.</param>
    public void UnregisterAllTriggers(IMacro macro)
    {
        foreach (var handlers in EventHandlers.Values)
            handlers.RemoveAll(tf => tf.Macro.Id == macro.Id);
    }

    /// <summary>
    /// Raises a trigger event to all registered macros and functions.
    /// </summary>
    /// <param name="eventType">The type of event to raise.</param>
    /// <param name="data">Optional data associated with the event.</param>
    public async Task RaiseTriggerEvent(TriggerEvent eventType, object? data = null)
    {
        if (!EventHandlers.TryGetValue(eventType, out var handlers) || handlers.Count == 0)
            return;

        var args = new TriggerEventArgs(eventType, data);
        foreach (var triggerFunction in handlers.ToList())
        {
            try
            {
                // For OnAddonEvent, check if the addon name and event type match
                if (eventType == TriggerEvent.OnAddonEvent && data is { } eventData)
                {
                    var addonName = eventData.GetType().GetProperty("AddonName")?.GetValue(eventData) as string;
                    var addonEventType = eventData.GetType().GetProperty("EventType")?.GetValue(eventData) as string;

                    if (addonName != triggerFunction.AddonName || addonEventType != triggerFunction.AddonEventType)
                        continue;
                }

                if (string.IsNullOrEmpty(triggerFunction.FunctionName))
                {
                    // Macro-level trigger: raise the event for the entire macro
                    if (FrameworkLogger.VerboseEnabled)
                        FrameworkLogger.Verbose($"Raising trigger event {eventType} for macro {triggerFunction.Macro.Name}");
                    TriggerEventOccurred?.Invoke(triggerFunction.Macro, args);
                }
                else
                {
                    // For function-level triggers, request function execution via event (doing this to avoid circular dependencies)
                    if (triggerFunction.Macro.Type == MacroType.Lua)
                    {
                        if (FrameworkLogger.VerboseEnabled)
                            FrameworkLogger.Verbose($"Requesting function execution for {triggerFunction.FunctionName} in macro {triggerFunction.Macro.Name}");
                        FunctionExecutionRequested?.Invoke(this, new FunctionExecutionRequestedEventArgs(triggerFunction.Macro.Id, triggerFunction.FunctionName, args));
                    }
                    else
                        // I could technically support this, but I don't think it's necessary
                        throw new NotSupportedException($"[{nameof(TriggerEventManager)}] Trigger event handling for {triggerFunction.Macro.Type} macros is not supported.");
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Error handling trigger event {eventType} for macro {triggerFunction.Macro.Name} function {triggerFunction.FunctionName}: {ex}");
            }
        }
    }

    /// <summary>
    /// Disposes of the trigger event manager.
    /// </summary>
    public void Dispose() => EventHandlers.Clear();
}
