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

Cами разработчики .NET в комментарии здесь и написали: "если вызывающий хочет продолжить в текущем контексте/TaskScheduler, и они не default".
Мы не модифицировали TaskScheduler, но SynchronizationContext у нас как раз будет кастомный, поэтому мы попадём в данную ветвь и создадим `SynchronizationContextAwaitTaskContinuation`:
````
if (SynchronizationContext.Current is SynchronizationContext syncCtx && syncCtx.GetType() != typeof(SynchronizationContext))
{
    tc = new SynchronizationContextAwaitTaskContinuation(syncCtx, stateMachineBox.MoveNextAction, flowExecutionContext: false);
    goto HaveTaskContinuation;
}
````

То есть по Continuation'ам будет гулять уже не `IAsyncStateMachineBox`, а `TaskContinuation`. В результате, когда у таска вызовется TrySetResult, мы попадём не напрямую в IAsyncStateMachineBox.MoveNext(),
а в TaskContinuation:

````
switch (continuationObject) // Task.cs, CS: 3470
{
    case IAsyncStateMachineBox stateMachineBox:
        AwaitTaskContinuation.RunOrScheduleAction(stateMachineBox, canInlineContinuations);
        LogFinishCompletionNotification();
        return;

    case Action action:
        AwaitTaskContinuation.RunOrScheduleAction(action, canInlineContinuations);
        LogFinishCompletionNotification();
        return;
        
    case TaskContinuation tc:
        tc.Run(this, canInlineContinuations);
        LogFinishCompletionNotification();
        return;

    case ITaskCompletionAction completionAction:
        RunOrQueueCompletionAction(completionAction, canInlineContinuations);
        LogFinishCompletionNotification();
        return;
}
````

Т.к. мы `TaskContinuation`, мы попадаем сюда:
````
case TaskContinuation tc: // Task.cs, CS: 3488
    tc.Run(this, canInlineContinuations);
    LogFinishCompletionNotification();
    return;
````