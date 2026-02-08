Что, если `SetResult()` `async Task`, и, по цепочке, `MoveNext()` стейт машины никогда не вызовут? Она навсегда останется лежать в управляемой куче?

Зависит от того, ссылаемся ли мы на `Task`.

Если написать вот так:
````csharp
private static List<TaskCompletionSource<bool>> _tcsList = [];

public static void Test()
{
    Console.WriteLine($"Start: {GC.GetTotalMemory(true) / 1024} KB");
    
    Leak();
    
    Console.WriteLine($"After Leak: {GC.GetTotalMemory(false) / 1024} KB");
    
    for (int i = 0; i < 5; i++)
    {
        Thread.Sleep(1000);
        Console.WriteLine($"Tick {i}: {GC.GetTotalMemory(true) / 1024} KB");
    }
}

private static void Leak()
{
    var tcs = new TaskCompletionSource<bool>();
    _tcsList.Add(tcs);

    MyMethod(tcs.Task);
}

private static async void MyMethod(Task<bool> task) 
{
    var hugeData = new byte[1024 * 1024 * 100]; // 100 MB
    await task; 
    Console.WriteLine(hugeData.Length);
}
````

Вывод программы будет следующим:
````
Start: 272 KB
After Leak: 102680 KB
Tick 0: 102675 KB
Tick 1: 102675 KB
Tick 2: 102675 KB
Tick 3: 102675 KB
Tick 4: 102675 KB

Process finished with exit code 0.
````

Цепочка ссылок выглядит следующим образом: `Static List` -> `TaskCompletionSource` -> `Task` (ссылочный тип внутри `TaskCompletionSource`) -> `Continuations` (ссылочный тип внутри `Task`)
-> `AsyncStateMachineBox` (стейт машина по методу `MyMethod`) -> массив `hugeData`.

Этого можно избежать, если удалить `TaskCompletionSource` из списка - тогда мы порвём связь с GC Root:
````csharp
private static void Leak()
{
    var tcs = new TaskCompletionSource<bool>();
    _tcsList.Add(tcs);

    MyMethod(tcs.Task);
    _tcsList.Remove(tcs);
}
...
Start: 272 KB
After Leak: 102680 KB
Tick 0: 274 KB
Tick 1: 274 KB
Tick 2: 274 KB
Tick 3: 274 KB
Tick 4: 274 KB

Process finished with exit code 0.
````

Альтернативно - можно вызвать `TrySetResult()`:
````csharp
private static void Leak()
{
    var tcs = new TaskCompletionSource<bool>();
    _tcsList.Add(tcs);

    MyMethod(tcs.Task);
    tcs.TrySetResult(true);
}
````

Стейт машина метода `MyMethod` внутри выглядит вот так:
````csharp
[CompilerGenerated]
  [StructLayout(LayoutKind.Auto)]
  private struct <MyMethod>d__3 : 
  /*[Nullable(0)]*/
  IAsyncStateMachine
  {
    public int <>1__state;
    public AsyncVoidMethodBuilder <>t__builder;
    [Nullable(0)]
    public Task<bool> task;
    [Nullable(0)]
    private byte[] <hugeData>5__2;
    [Nullable(0)]
    private TaskAwaiter<bool> <>u__1;

    void IAsyncStateMachine.MoveNext()
    {
      int num1 = this.<>1__state;
      try
      {
        TaskAwaiter<bool> awaiter;
        int num2;
        if (num1 != 0)
        {
          this.<hugeData>5__2 = new byte[104857600 /*0x06400000*/];
          awaiter = this.task.GetAwaiter();
          if (!awaiter.IsCompleted)
          {
            this.<>1__state = num2 = 0;
            this.<>u__1 = awaiter;
            this.<>t__builder.AwaitUnsafeOnCompleted<TaskAwaiter<bool>, OrchestratingMemoryLeak.<MyMethod>d__3>(ref awaiter, ref this);
            return;
          }
        }
        else
        {
          awaiter = this.<>u__1;
          this.<>u__1 = new TaskAwaiter<bool>();
          this.<>1__state = num2 = -1;
        }
        awaiter.GetResult();
        Console.WriteLine(this.<hugeData>5__2.Length);
      }
      catch (Exception ex)
      {
        this.<>1__state = -2;
        this.<hugeData>5__2 = (byte[]) null;
        this.<>t__builder.SetException(ex);
        return;
      }
      this.<>1__state = -2;
      this.<hugeData>5__2 = (byte[]) null;
      this.<>t__builder.SetResult();
    }

    [DebuggerHidden]
    void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
    {
      this.<>t__builder.SetStateMachine(stateMachine);
    }
  }
````

Когда стейт машина переходит в состояние "завершено", она прямо затирает ссылку на массив:
````csharp
this.<hugeData>5__2 = (byte[]) null;
````

Поэтому GC подчищает этот массив и высвобождает память.

При этом сама стейт машина тоже подчистится - `Task`, при завершении, затирает ссылку на объект continuations:
````csharp
internal void FinishContinuations() // Task.cs, CS: 3445
{
    // Вот тут мы забираем continuationObject как локальный объект, а ссылку внутри Task затираем на s_taskCompletionSentinel
    object? continuationObject = Interlocked.Exchange(ref m_continuationObject, s_taskCompletionSentinel);
    if (continuationObject != null)
    {
        RunContinuations(continuationObject);
    }
}
````

Т.е. в сухом остатке в нашем листе останутся `TaskCompletionSource<bool>`, и `Task`, ссылку на который держит `TaskCompletionSource`.

Выходит, что для того чтобы стейт машина создала утечку, должны выполниться два условия:
1. Мы должны ссылаться на стейт машину, напрямую или через `Task`.
2. У Task никогда не должен вызваться `FinishContinuations()`.