Пример утечки памяти, похожий по своей механике на главы 6_0 и 6_1: `CancellationToken`.

Когда мы делаем `CancellationToken.Register(...)`, под капотом происходит следующее:
````csharp
internal CancellationTokenRegistration Register( // CancellationTokenSource.cs, CS: 578
    Delegate callback, object? stateForCallback, SynchronizationContext? syncContext, ExecutionContext? executionContext)
{
    ...
    if (!IsCancellationRequested)
    {
        ...
        Registrations? registrations = Volatile.Read(ref _registrations);
        if (registrations is null)
        {
            // Создали регистрации если их ещё нет, либо взяли
            registrations = new Registrations(this);
            registrations = Interlocked.CompareExchange(ref _registrations, registrations, null) ?? registrations;
        }

        CallbackNode? node = null;
        long id = 0;
        if (registrations.FreeNodeList is not null)
        {
            registrations.EnterLock();
            try
            {
                node = registrations.FreeNodeList;
                if (node is not null)
                {
                    Debug.Assert(node.Prev == null, "Nodes in the free list should all have a null Prev");
                    registrations.FreeNodeList = node.Next;
                    
                    // Создали ноду с коллбеком, который мы передали в CancellationToken.Register(), на основе уже созданной (пулированной) ноды
                    node.Id = id = registrations.NextAvailableId++;
                    node.Callback = callback;
                    node.CallbackState = stateForCallback;
                    node.ExecutionContext = executionContext;
                    node.SynchronizationContext = syncContext;
                    node.Next = registrations.Callbacks;
                    registrations.Callbacks = node;
                    if (node.Next != null)
                    {
                        node.Next.Prev = node;
                    }
                }
            }
            finally
            {
                registrations.ExitLock();
            }
        }

        if (node is null)
        {
            // Создали ноду с коллбеком, который мы передали в CancellationToken.Register(), с нуля
            node = new CallbackNode(registrations);

            node.Callback = callback;
            node.CallbackState = stateForCallback;
            node.ExecutionContext = executionContext;
            node.SynchronizationContext = syncContext;

            registrations.EnterLock();
            try
            {
                node.Id = id = registrations.NextAvailableId++;
                node.Next = registrations.Callbacks;
                if (node.Next != null)
                {
                    node.Next.Prev = node;
                }
                registrations.Callbacks = node;
            }
            finally
            {
                registrations.ExitLock();
            }
        }
        ...
    }
    ...
}
````
`CancellationToken` берёт и регистрирует наш коллбек в список регистраций внутри `CancellationTokenSource`. Так зарождается ссылка на делегат.

Возьмём следующий пример:
````csharp
public static class CancellationTokenExample
{
    private static CancellationTokenSource _cts = new CancellationTokenSource();
    
    public static async Task RunCancellationTokenExample()
    {
        Console.WriteLine($"Start: {GC.GetTotalMemory(true) / 1024} KB");

        await DoWork(_cts.Token);
        
        Console.WriteLine($"After DoWork: {GC.GetTotalMemory(false) / 1024} KB");

        GcCollect();
        
        Console.WriteLine($"After GC collect (LEAK): {GC.GetTotalMemory(false) / 1024} KB");
        
        await _cts.CancelAsync();
        
        GcCollect();
        Console.WriteLine($"After cancel: {GC.GetTotalMemory(false) / 1024} KB");
    }

    private static async Task DoWork(CancellationToken token)
    {
        var heavyData = new byte[1024 * 1024 * 100];

        token.Register(() => Console.WriteLine($"Галя, у нас отмена! {heavyData.Length}")); 

        await Task.Delay(1000, token);
        
        Console.WriteLine($"Heavy data: {heavyData.Length}");
    }

    private static void GcCollect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
````
Здесь мы:
1. Создали статический `CancellationTokenSource`.
2. Вызвали асинхронный метод `DoWork` и отдали ему `CancellationToken token`. 
3. Внутри, стейт машина метода `DoWork` через анонимную функцию захватит массив байтов весом 100мб:
````csharp
  [CompilerGenerated]
  private sealed class <>c__DisplayClass2_0
  {
    [Nullable(0)]
    public byte[] heavyData;

    public <>c__DisplayClass2_0()
    {
      base..ctor();
    }

    internal void <DoWork>b__0()
    {
      DefaultInterpolatedStringHandler interpolatedStringHandler = new DefaultInterpolatedStringHandler(20, 1);
      interpolatedStringHandler.AppendLiteral("Галя, у нас отмена! ");
      interpolatedStringHandler.AppendFormatted<int>(this.heavyData.Length);
      Console.WriteLine(interpolatedStringHandler.ToStringAndClear());
    }
  }
...
  [CompilerGenerated]
  [StructLayout(LayoutKind.Auto)]
  private struct <DoWork>d__2 : 
  /*[Nullable(0)]*/
  IAsyncStateMachine
  {
    public int <>1__state;
    public AsyncTaskMethodBuilder <>t__builder;
    public CancellationToken token;
    [Nullable(0)]
    private CancellationTokenExample.<>c__DisplayClass2_0 <>8__1;
    private TaskAwaiter <>u__1;

    void IAsyncStateMachine.MoveNext()
    {
      int num1 = this.<>1__state;
      try
      {
        TaskAwaiter awaiter;
        int num2;
        if (num1 != 0)
        {
          // Создали экземпляр класса, сгенерированного по лямбде, и присвоили ему массив байтов.
          // Здесь любопытен тот момент, что компилятор, видя использование массива байтов только в анонимном классе, даже не аллоцирует локальный массив, как это выглядит в нашем коде.
          // Вместо этого он делает new прямо в единственном месте использования - в c__DisplayClass2_0
          this.<>8__1 = new CancellationTokenExample.<>c__DisplayClass2_0();
          this.<>8__1.heavyData = new byte[104857600 /*0x06400000*/];
          this.token.Register(new Action((object) this.<>8__1, __methodptr(<DoWork>b__0)));
          awaiter = Task.Delay(1000, this.token).GetAwaiter();
          if (!awaiter.IsCompleted)
          {
            this.<>1__state = num2 = 0;
            this.<>u__1 = awaiter;
            this.<>t__builder.AwaitUnsafeOnCompleted<TaskAwaiter, CancellationTokenExample.<DoWork>d__2>(ref awaiter, ref this);
            return;
          }
        }
        else
        {
          awaiter = this.<>u__1;
          this.<>u__1 = new TaskAwaiter();
          this.<>1__state = num2 = -1;
        }
        awaiter.GetResult();
        DefaultInterpolatedStringHandler interpolatedStringHandler = new DefaultInterpolatedStringHandler(12, 1);
        interpolatedStringHandler.AppendLiteral("Heavy data: ");
        interpolatedStringHandler.AppendFormatted<int>(this.<>8__1.heavyData.Length);
        Console.WriteLine(interpolatedStringHandler.ToStringAndClear());
      }
      catch (Exception ex)
      {
        this.<>1__state = -2;
        this.<>8__1 = (CancellationTokenExample.<>c__DisplayClass2_0) null;
        this.<>t__builder.SetException(ex);
        return;
      }
      this.<>1__state = -2;
      this.<>8__1 = (CancellationTokenExample.<>c__DisplayClass2_0) null;
      this.<>t__builder.SetResult();
    }

    [DebuggerHidden]
    void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
    {
      this.<>t__builder.SetStateMachine(stateMachine);
    }
  }
````
4. После этого мы дождёмся пока асинхронный `DoWork` завершится, принудительно вызовем сборщик и... Увидим, что память не очистилась. Вывод программы:
````
Start: 275 KB
Heavy data: 104857600
After DoWork: 102723 KB
After GC collect (LEAK): 102688 KB
Галя, у нас отмена! 104857600
After cancel: 294 KB

Process finished with exit code 0.
````

Так произошло именно потому, что мы держим ссылку на `DisplayClass` через список `Registrations` у `CancellationTokenSource`. Как только мы вызвали `token.Cancel()`, произошла очистка
коллбеков, и второй проход `GC` смог избавиться от `DisplayClass` вместе с его массивом байтов, что видно по логу: `After cancel: 294 KB`.

При этом сделать так:
````
_cts = null;
GcCollect();
````
Не поможет, до тех пор пока жива стейт машина `RunCancellationTokenExample`:
````
Start: 275 KB
Heavy data: 104857600
After DoWork: 102723 KB
After GC collect (LEAK): 102688 KB
After null-out: 102688 KB

Process finished with exit code 0.
````

Тогда можно дождаться пока стейт машина завершится. Вызываем сборщик ещё раз, и убеждаемся что зануление ссылки помогло только после того, как стейт машина стала Eligible for GC:
````csharp
public static async Task Main(string[] args)
{ 
    await CancellationTokenExample.RunCancellationTokenExample();
    await Task.Delay(1000);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    
    Console.WriteLine($"After force GC in Main: {GC.GetTotalMemory(false) / 1024} KB");
}
````

Вывод:
````
Start: 275 KB
Heavy data: 104857600
After DoWork: 102723 KB
After GC collect (LEAK): 102688 KB
After null-out: 102688 KB
After force GC in Main: 292 KB

Process finished with exit code 0.
````

До `await Task.Delay(1000)`, цепочка зависимостей выглядела так: `<Main>d__0 : IAsyncStateMachine --> private TaskAwaiter <>u__1; --> CancellationTokenExample.Task`.
Стейт машина `Main` держала ссылку на `Task`, который после вызова `AwaitUnsafeOnCompleted` стейт машины `<RunCancellationTokenExample>d__1` превратился `AsyncStateMachineBox` в куче.
Если посмотреть на тип, вернувшийся после вызова RunCancellationTokenExample, это будет именно AsyncStateMachineBox:
````csharp
public static async Task Main(string[] args)
{ 
    var t = CancellationTokenExample.RunCancellationTokenExample();
    Console.WriteLine(t.GetType());
}
````
Вывод:
````
Start: 275 KB
System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1+AsyncStateMachineBox`1[System.Threading.Tasks.VoidTaskResult,AsyncResearch.AsyncExperiments.Chapter_6_AsyncAndGarbageCollector.Source.CancellationTokenExample+<RunCancellationTokenExample>d__1]

Process finished with exit code 0.
````

Когда стейт машина `RunCancellationTokenExample` перейдёт в состояние -2, стейт машина `Main()` буквально занулила ссылку: `this.<>u__1 = new TaskAwaiter();`. Таким образом, трюк
с `_cts = null;` наконец сработал.