В .NET существует Legacy способ построения асинхронных методов: `Task.ContinueWith()`:
````csharp
public static async Task RunContinueWithExample()
{
    await Task.Delay(99999).ContinueWith(task => Console.WriteLine("Hello, World!"));
}
````

Под капотом данный механизм работает предельно просто:
1. `Task.Delay(99999)` в примере возвращает нам `Task`.
2. Мы вызываем у `Task` - `ContinueWith`. В этот момент `Task` может быть как завершённым, так и не завершённым.
3. Мы проверяем: если Task завершён, сразу вызови продолжение. Если нет, добавь делегат в `Continuations` к таску.
````csharp
// Attempt to enqueue the continuation
bool continuationQueued = AddTaskContinuation(continuation, addBeforeOthers: false); // Task.cs, CS: 4519

// If the continuation was not queued (because the task completed), then run it now.
if (!continuationQueued) continuation.Run(this, canInlineContinuationTask: true);
````
4. Когда таск завершится, наш делегат вызовется в `Continuations`.

В отличие от обыного await, где при наличии контекста синхронизации, рантайм захватит его и создаст `SynchronizationContextAwaitTaskContinuation`, ContinueWith так не сделает:
он просто создаст Task и подпишется в `Continuations`, который по умолчанию направляется в `ThreadPool`. В методе RunContinuations, мы, будучи `ContinueWithTaskContinuation`, 
попадём вот в этот блок:
````csharp
case TaskContinuation tc: // Task.cs, CS: 3488
    tc.Run(this, canInlineContinuations);
    LogFinishCompletionNotification();
    return;
````

`tc.Run()` означает, что мы выполнимся на том же потоке, где была завершена таска. В случае с `Task.Delay` это `ThreadPool`.

У `ContinueWith` есть и другие минусы. Например, он каждый раз создаёт Task, даже если ожидание выполнится синхронно:
````csharp
Task continuationTask = new ContinuationTaskFromTask( // Task.cs, CS: 3789
    this, continuationAction, null,
    creationOptions, internalOptions
);
````

Также нам пришлось бы вручную обработать исключение в `Task`, если в делегат `ContinueWith` прилетел таск с исключением, ведь уже нет стейт машины, которая делает это автоматически как
в `await`. Всё это привело к тому, что `ContinueWith` больше не используется и является легаси функционалом.