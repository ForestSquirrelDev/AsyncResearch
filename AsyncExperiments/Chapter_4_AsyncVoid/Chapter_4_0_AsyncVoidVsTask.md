Возьмём следующий пример:
````
public static class AsyncVoidExample
{
    public static void Test()
    {
        Console.WriteLine("Start");
        DoWorkAsync();
        Console.WriteLine("End");
    }
    
    public static async void DoWorkAsync()
    {
        var localVariable = 42;
        await Task.Delay(10000);
        Console.WriteLine("Resumed with " + localVariable);
    }
}
````

Метод `DoWorkAsync()` не возвращает Task, как это делают методы с сигнатурой `async Task`:

````
[AsyncStateMachine(typeof (AsyncVoidExample.<DoWorkAsync>d__1))]
public static void DoWorkAsync()
{
  AsyncVoidExample.<DoWorkAsync>d__1 stateMachine;
  stateMachine.<>t__builder = AsyncVoidMethodBuilder.Create();
  stateMachine.<>1__state = -1;
  // async Task бы вернул здесь builder.Task
  stateMachine.<>t__builder.Start<AsyncVoidExample.<DoWorkAsync>d__1>(ref stateMachine);
}
````

Для `DoWorkAsync()` создаётся стейт машина - аналогично методам с сигнатурой `async Task`:
````
[CompilerGenerated]
[StructLayout(LayoutKind.Auto)]
private struct <DoWorkAsync>d__1 : IAsyncStateMachine
{
  public int <>1__state;
  public AsyncVoidMethodBuilder <>t__builder;
  private int <localVariable>5__2;
  private TaskAwaiter <>u__1;
...
````

С той лишь разницей, что вместо `AsyncTaskMethodBuilder` используется `AsyncVoidMethodBuilder`.

Внутри у `AsyncVoidMethodBuilder` много общего с `AsyncTaskMethodBuilder` - в прямом смысле. `AsyncVoidMethodBuilder` хранит экземпляр `AsyncTaskMethodBuilder`:
````
public struct AsyncVoidMethodBuilder
{
    private SynchronizationContext? _synchronizationContext; // AsyncVoidMethodBuilder.cs, CS: 17
    private AsyncTaskMethodBuilder _builder;
    ...
...
````

За счёт этого `AsyncVoidMethodBuilder` может использовать всё тот же метод `AwaitUnsafeOnCompleted`, когда в стейт машине встречается незавершённый `TaskAwaiter` - он просто проксирует вызов к `AsyncTaskMethodBuilder`:
````
public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>( // AsyncVoidMethodBuilder.cs, CS: 69
    ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine =>
    _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
````

Аналогично, `AsyncVoidMethodBuilder` использует общий с `AsyncTaskMethodBuilder` helper - `AsyncMethodBuilderCore`:
````
[DebuggerStepThrough] // AsyncVoidMethodBuilder.cs, CS: 37
[MethodImpl(MethodImplOptions.AggressiveInlining)]    
public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
    AsyncMethodBuilderCore.Start(ref stateMachine);
````

Главные отличия `AsyncVoidMethodBuilder` заключаются в следующем:

#### Первое - метод `Create()`.

Внутри него AsyncVoidMethodBuilder сообщает SynchronizationContext о начале выполнения асинхронной операции:
````
public static AsyncVoidMethodBuilder Create() // AsyncVoidMethodBuilder.cs, CS: 23
{
    SynchronizationContext? sc = SynchronizationContext.Current; 
    sc?.OperationStarted();

    return new AsyncVoidMethodBuilder() { _synchronizationContext = sc };
}
````

#### Второе - `SetResult()`.

Когда стейт машина `DoWorkAsync()` вызовет у `builder` метод `SetResult()`, тот проставит SetResult() у своего экземпляра `AsyncTaskMethodBuilder`.
Причём он сделает это независимо от того, завершилась таска успехом, или нет:
````
_builder.SetResult(); // AsyncVoidMethodBuilder.cs, CS: 99
````

#### Третье - `SetException()`.

В отличие от `AsyncTaskMethodBuilder`, `AsyncVoidMethodBuilder` не сохраняет исключение внутрь `Task`, чтобы вызывающий сам решил, как и когда ему обрабатывать
(или не обрабатывать) лежащее там исключение.

`AsyncVoidMethodBuilder` сразу пытается выбросить исключение: и делает он это, в зависимости от наличия `SynchronizationContext`- либо прямо в контекст:
````
SynchronizationContext? context = _synchronizationContext; AsyncVoidMethodBuilder.cs, CS: 123
if (context != null)
{
    // and decrement its outstanding operation count.
    try
    {
        Task.ThrowAsync(exception, targetContext: context);
    }
    finally
    {
        NotifySynchronizationContextOfCompletion(context);
    }
}
````

Либо в `ThreadPool`:
````
ThreadPool.QueueUserWorkItem(static state => ((ExceptionDispatchInfo)state!).Throw(), edi); // Task.cs, CS: 1929
````

Как пишут сами разработчики .NET, `This will result in a crash unless legacy exception behavior is enabled by a config file or a CLR host.`

Получается, что `async void` - это хитрое переиспользование `Task`, только с сигнатурой `void`, и более агрессивным выбросом исключений.