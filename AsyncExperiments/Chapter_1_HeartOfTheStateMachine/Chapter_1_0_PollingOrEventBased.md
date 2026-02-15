#### Когда мы делаем "await" внутри асинхронного метода, в какой момент выполняется "продолжение" кода после await? Какой механизм отвечает за это - некая Polling-based система, или это всё же Event-based механизм?

Возьмём пример:

````csharp
public static async Task Main(string[] args)
{
    await ContinuationExample.Test();
    Console.WriteLine("Hello, World!");
}

public static async Task Test()
{
    Console.WriteLine("Start");
    await DoWorkAsync();
    Console.WriteLine("End");
}

public static async Task DoWorkAsync()
{
    var localVariable = 42;
    await Task.Delay(100);
    Console.WriteLine("Resumed with " + localVariable);
}
````

Асинхронный метод `Test()`, возвращающий `Task`, вызывается в асинхронном `Program.cs`.
Внутри `Test()` вызывается другой асинхронный метод: `DoWorkAsync()`. Там захватывается локальная переменная типа `int`, выполняется ожидание в 100 миллисекунд, и выводится сообщение в консоль.

### Разбор

Ещё до того, как выполнится первая строчка кода в рантайме, компилятор C# превращает класс `Program` в стейт машину. Было:

````csharp
using AsyncResearch.AsyncExperiments.ChapterOne;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await ContinuationExample.Test();
            Console.WriteLine("Hello, World!");
        }
    }
}
````

Стало:
````csharp
// Decompiled with JetBrains decompiler
// Type: AsyncResearch.Program
// Assembly: AsyncResearch, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 34DDB2C4-18AF-4A1F-98CE-FA2060C6A76A
// Assembly location: D:\work\AsyncResearch\bin\Release\net8.0\AsyncResearch.dll
// Local variable names from D:\work\AsyncResearch\bin\Release\net8.0\AsyncResearch.pdb
// Compiler-generated code is shown

using AsyncResearch.AsyncExperiments.ChapterOne;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AsyncResearch;

public class Program
{
  [NullableContext(1)]
  [AsyncStateMachine(typeof (Program.<Main>d__0))]
  public static Task Main(string[] args)
  {
    Program.<Main>d__0 stateMachine;
    stateMachine.<>t__builder = AsyncTaskMethodBuilder.Create();
    stateMachine.<>1__state = -1;
    stateMachine.<>t__builder.Start<Program.<Main>d__0>(ref stateMachine);
    return stateMachine.<>t__builder.Task;
  }

  public Program()
  {
    base..ctor();
  }

  [SpecialName]
  private static void <Main>([Nullable(1)] string[] args)
  {
    Program.Main(args).GetAwaiter().GetResult();
  }

  [CompilerGenerated]
  [StructLayout(LayoutKind.Auto)]
  private struct <Main>d__0 : IAsyncStateMachine
  {
    public int <>1__state;
    public AsyncTaskMethodBuilder <>t__builder;
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
          awaiter = ContinuationExample.Test().GetAwaiter();
          if (!awaiter.IsCompleted)
          {
            this.<>1__state = num2 = 0;
            this.<>u__1 = awaiter;
            this.<>t__builder.AwaitUnsafeOnCompleted<TaskAwaiter, Program.<Main>d__0>(ref awaiter, ref this);
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
        Console.WriteLine("Hello, World!");
      }
      catch (Exception ex)
      {
        this.<>1__state = -2;
        this.<>t__builder.SetException(ex);
        return;
      }
      this.<>1__state = -2;
      this.<>t__builder.SetResult();
    }

    [DebuggerHidden]
    void IAsyncStateMachine.SetStateMachine([Nullable(1)] IAsyncStateMachine stateMachine)
    {
      this.<>t__builder.SetStateMachine(stateMachine);
    }
  }
}
````

Компилятор сгенерировал для метода класса Program стейт машину `<Main>d__0`. Это структура, реализующая интерфейс [IAsyncStateMachine](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/IAsyncStateMachine.cs):

````csharp
namespace System.Runtime.CompilerServices
{
    public interface IAsyncStateMachine
    {
        void MoveNext();
        void SetStateMachine(IAsyncStateMachine stateMachine);
    }
}
````

Здесь интересен тот момент, что операционной системе и .NET нужна синхронная точка входа нашей программы, поэтому когда мы объявили `public static async Task Main(string[] args)`, он не стал истинной точкой входа. Компилятор сгенерировал "настоящую" точку входа с другой сигнатурой, которая синхронно вызывает наш `async Main`:
````csharp
[SpecialName]
private static void <Main>([Nullable(1)] string[] args)
{
  Program.Main(args).GetAwaiter().GetResult();
}
````

Внутри стейт машины, созданы три поля:
- `int` state
- `AsyncTaskMethodBuilder` builder
- `TaskAwaiter` u__1

В поле `<>t__builder` проставляется инстанс структуры [AsyncTaskMethodBuilderT](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs):
````csharp
stateMachine.<>t__builder = AsyncTaskMethodBuilder.Create();
...

public static AsyncTaskMethodBuilder<TResult> Create() => default; // AsyncTaskMethodBuilder<TResult>, CS: 27
````

Затем, у стейт машины состояние сразу выставляется в -1: этот стейт означает исполнение с начала метода.
````csharp
stateMachine.<>1__state = -1;
````
Когда исполнение дойдёт до ожидания первого `await`, стейт выставится в 0. До второго `await`: 1. До третьего `await`: 2, и так далее.
Стейт -2 будет означать что асинхронная стейт машина завершила работу.

Наконец, мы просим `<>t__builder` запустить стейт машину вызовом метода `Start(ref stateMachine)`, и возвращаем вызывающему `Task`.
Через Builder вызов Start проксируется в [AsyncMethodBuilderCore](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncMethodBuilderCore.cs). Там происходит первый вызов `MoveNext()` стейт машины:
````csharp
...
try
{
    stateMachine.MoveNext();
}
...
````

Через num1 мы копируем стейт в локальный стек испольнения, т.к. IL инструкция чтения из стека значительно дешевле чтения переменной из полей структуры:
````csharp
int num1 = this.<>1__state;
````

Внутри метода `MoveNext()` мы проверяем что `num1` не равен нулю: 0 в контексте стейт машины означал бы, что мы ожидаем первый await. 
Но т.к. мы его ещё не ожидаем, а находимся в стейте -1 (_начало работы_), мы попадаем в данную ветвь:
````csharp
if (num1 != 0)
{
  awaiter = ContinuationExample.Test().GetAwaiter();
  if (!awaiter.IsCompleted)
  {
    this.<>1__state = num2 = 0;
    this.<>u__1 = awaiter;
    this.<>t__builder.AwaitUnsafeOnCompleted<TaskAwaiter, Program.<Main>d__0>(ref awaiter, ref this);
    return;
  }
}
````

Внутри неё мы просим `Task` создать нам TaskAwaiter: 
````csharp
awaiter = ContinuationExample.Test().GetAwaiter();
````
И проверяем, что он не Completed. 

[TaskAwaiter](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/TaskAwaiter.cs) это readonly структура, которая в конструкторе проверяет что ей не передали `null` вместо `Task`, и записывает `task` к себе в поля:
````
internal TaskAwaiter(Task task)
{
    Debug.Assert(task != null, "Constructing an awaiter requires a task to await.");
    m_task = task;
}
````

Весь этот код пока что выполняется синхронно. При вызове метода `Test()`, происходит такая же цепочка: создаётся стейт машина, создаётся `Builder`, у `Builder` вызывается `Start()`. 
`Start()` вызывает `MoveNext()`, и тот первым делом синхронно пишет:
````csharp
Console.WriteLine("Start");
````
Вот так это выглядит в Low-Level C#:
````csharp
...
TaskAwaiter awaiter;
int num2;
if (num1 != 0)
{
  Console.WriteLine("Start");
  awaiter = ContinuationExample.DoWorkAsync().GetAwaiter();
...
````
И дойдя до `await`, `Test()` тоже делает `GetAwaiter()`, который по цепочке синхронно вызывает уже `DoWorkAsync()`:
````csharp
public static async Task DoWorkAsync()
{
    var localVariable = 42;
    await Task.Delay(100);
    Console.WriteLine("Resumed with " + localVariable);
}
````
В low-level C# для метода DoWorkAsync() создаётся стейт машина, которая записывает в поля структуры локальную переменную: `this.<localVariable>5__2 = 42;`. Затем - берёт `TaskAwaiter` у `Task.Delay()`:
````csharp
...
awaiter = Task.Delay(10000).GetAwaiter();
...
````
Если бы awaiter оказался моментально завершён, наш код бы выполнился синхронно - мы сразу укажем стейт "завершён" и отдадим результат в Builder:
````csharp
...
this.<>1__state = -2;
this.<>t__builder.SetResult();
...
````
Однако мы ждём `Delay(10000)`, и `awaiter` не завершён. Поэтому во всём нашем примере это будет первый вызов `await`, который не сможет завершиться синхронно, и мы наконец попадём в `AwaitUnsafeOnCompleted`:
````csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)] // AsyncTaskMethodBuilderT.cs, CS: 86
internal static void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine, [NotNull] ref Task<TResult>? taskField)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
{
    IAsyncStateMachineBox box = GetStateMachineBox(ref stateMachine, ref taskField);
    AwaitUnsafeOnCompleted(ref awaiter, box);
}
````
Здесь создаётся `AsyncStateMachineBox<TStateMachine>`. Это класс, и его название не случайно. Он в буквальном смысле используется для упаковки стейт машины в управляемую кучу, чтобы потом продолжить исполнение кода после `await`:
````csharp
...
box.StateMachine = stateMachine; // AsyncTaskMethodBuilderT, CS: 225
box.Context = currentContext;
...
return box;
...
````

Через `Builder`, вызов `AwaitUnsafeOnCompleted` передаётся в `TaskAwaiter`:
````csharp
internal static void UnsafeOnCompletedInternal(Task task, IAsyncStateMachineBox stateMachineBox, bool continueOnCapturedContext) // TaskAwaiter.cs, CS: 193
{
    ...
    else
    {
        task.UnsafeSetContinuationForAwait(stateMachineBox, continueOnCapturedContext);
    }
    ...
}
````

Если `Task` уже `Completed`, мы сразу попросим `ThreadPool` выполнить continuation, как только появится свободный поток:
````csharp
...
if (!AddTaskContinuation(stateMachineBox, addBeforeOthers: false)) // Task.cs, CS: 2592
{
    ThreadPool.UnsafeQueueUserWorkItemInternal(stateMachineBox, preferLocal: true);
}
...
````

Но если таск не завершён, мы попадаем в метод `AddTaskContinuation`: 
````csharp
private bool AddTaskContinuation(object tc, bool addBeforeOthers) // Task.cs, CS: 4598
{
    Debug.Assert(tc != null);

    if (IsCompleted) return false;
    
    // Прихранили AsyncStateMachineBox.MoveNext() в m_continuationObject
    if ((m_continuationObject != null) || (Interlocked.CompareExchange(ref m_continuationObject, tc, null) != null))
    {
        return AddTaskContinuationComplex(tc, addBeforeOthers);
    }
    else return true;
}
````

Здесь вызов `Interlocked.CompareExchange(ref m_continuationObject, tc, null)` присваивает объект класса `AsyncStateMachineBox` с нашей стейт машиной внутри, в переменную `m_continuationObject` внутри объекта `Task`.

Таким образом, `AsyncStateMachineBox` кладётся в управляемую кучу до тех пор, пока операционная система и инфраструктура .NET не вызовут завершение таймера.
Это произойдёт со следующим стактрейсом:
````
Task.DelayPromise.CompleteTimedOut()
TimerQueueTimer.Fire()
TimerQueue.FireNextTimers()
ThreadPoolWorkQueue.Dispatch()
PortableThreadPool.WorkerThread.WorkerThreadStart()
````

В результате чего мы попадаем в метод `CompleteTimedOut` внутреннего вложенного класса `Task` - `DelayPromise`:
````csharp
private void CompleteTimedOut() // Task.cs, CS: 5735
{
    if (TrySetResult())
    {
        Cleanup();

        if (s_asyncDebuggingEnabled)
            RemoveFromActiveTasks(this);

        if (TplEventSource.Log.IsEnabled())
            TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Completed);
    }
}
````

В методе `TrySetResult()` мы попадаем в `RunContinuations()`, внутри которого есть свич, который видит что `continuationObject` - это `AsyncStateMachineBox`, и вызывает `RunOrScheduleAction`.

И если мы попали в Happy path, там вызывается `box.MoveNext()`:
````csharp
...
try // TaskContinuation.cs, CS: 795
{
    if (prevCurrentTask != null) currentTask = null;
    box.MoveNext();
}
...
````

Что, наконец, приведёт нас к завершающей части стейт машины метода `DoWorkAsync()`:
````csharp
...
else
{
  awaiter = this.<>u__1;
  this.<>u__1 = new TaskAwaiter();
  this.<>1__state = num2 = -1;
}
awaiter.GetResult();
Console.WriteLine(string.Concat("Resumed with ", this.<localVariable>5__2.ToString()));
...
````
Стейт снова выставится в -1, т.к. мы больше не ожидаем первый `await`. Ожидание завершится через `awaiter.GetResult()`, и мы выведем в консоль `"Resumed with 42"`.

Затем мы выставим состояние стейт машины `DoWorkAsync()` в -2 (_завершено_) и обратимся к `this.<>t__builder.SetResult();`. По цепочке вызовется `FinishContinuations()` для всех вызывающих стейт машин: методов `Test()` и `Main()`.

Они вызываются по цепочке, ведь каждый из двух асинхронных методов `public static async Task Main(string[] args)` и `public static async Task Test()`, не смог выполниться синхронно. `GetAwaiter()` возвращал им незавершённый `Awaiter`, и стейт машина, собранная из данных методов, каждый раз упаковывалась в `AsyncStateMachineBox` и клалась в управляемую кучу следующим образом:
1. `Main()` вызывает `Test()`. Метод `Test` создаёт новый `AsyncTaskMethodBuilderT`, у того внутри через Lazy инициализацию создаётся объект класса `Task<TResult>`. Task возвращается на вызове `Test()`, и у Task мы просим создать структуру `new TaskAwaiter()`: `ContinuationExample.Test().GetAwaiter()`.
2. Затем стейт машина `Main()` через свой собственный `AsyncTaskMethodBuilderT` говорит: `TaskAwaiter` метода `Test()` не завершён? Ну, пни меня, когда вот этот вот `TaskAwaiter` завершится - `this.<>t__builder.AwaitUnsafeOnCompleted<TaskAwaiter, Program.<Main>d__0>(ref awaiter, ref this);`, зачем собственно и передаётся ref this.
3. Теперь Task метода `Test()` знает, что надо пнуть `Main()`, когда он завершится, ведь под капотом `AsyncStateMachineBox` метода main записалась в `Task.m_continuationObject` метода `Test()`.
4. `Test()`, в свою очередь, вызывает `DoWorkAsync()`. Тот тоже возвращает ему `TaskAwaiter`, и стейт машина `Test()` тоже просит пнуть её через `m_continuationObject`, когда `Task` метода `DoWorkAsync()` завершится.
5. Теперь таска, созданная внутри стейт машины `DoWorkAsync()`, знает, что надо пнуть `Test()`.
6. В обратную сторону этот процесс работает так же: таска `Delay()`, созданная внутри стейт машины `DoWorkAsync()`, завершается. Смотрит в свой `m_continuationObject`. Там лежит `AsyncStateMachineBox` `DoWorkAsync`. Вызывается `DoWorkAsync.MoveNext()`: 
````csharp
...
try // TaskContinuation.cs, CS: 792
{
   if (prevCurrentTask != null) currentTask = null;
   box.MoveNext();
}
...
````
7. `DoWorkAsync()` завершает своё исполнение, ставит себе стейт в -2, и вызывает `this.<>t__builder.SetResult()`.
8. `SetResult()` вызывает `Task.RunContinuations()`, тот видит что в `m_continuationObject` лежит `Test()`, и вызывает уже у её коробки `MoveNext()`.
9. Тот дописывает в консоль "End", и аналогичным образом через `SetResult()` вызывает `AsyncStateMachineBox` уже для метода `Main()`.

`Main()` завершается. На этом этапе просыпается скрытая точка входа (`static void <Main>`), которая была заблокирована вызовом `GetAwaiter().GetResult()`:

````csharp
[SpecialName]
private static void <Main>([Nullable(1)] string[] args)
{
    Program.Main(args).GetAwaiter().GetResult();
}
````
Процесс завершается.

Итог: Асинхронность в C# — это хитрая событийная система, в которой код после `await` вызовется, когда задача, которую ожидает асинхронная стейт машина, завершится.