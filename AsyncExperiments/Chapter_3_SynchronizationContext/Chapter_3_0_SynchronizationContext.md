Когда Task уходит в ожидание через `AsyncStateMachineBox`, можно увидеть, что внутри происходит следующее:
````
// If the caller wants to continue on the current context/scheduler and there is one,
// fall back to using the state machine's delegate.
if (continueOnCapturedContext) // Task.cs, CS: 2575
{
    if (SynchronizationContext.Current is SynchronizationContext syncCtx && syncCtx.GetType() != typeof(SynchronizationContext))
    {
        tc = new SynchronizationContextAwaitTaskContinuation(syncCtx, stateMachineBox.MoveNextAction, flowExecutionContext: false);
        goto HaveTaskContinuation;
    }

    if (TaskScheduler.InternalCurrent is TaskScheduler scheduler && scheduler != TaskScheduler.Default)
    {
        tc = new TaskSchedulerAwaitTaskContinuation(scheduler, stateMachineBox.MoveNextAction, flowExecutionContext: false);
        goto HaveTaskContinuation;
    }
}
````

