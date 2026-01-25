## В данном исследовании я пробую ответить на вопрос: когда мы делаем "await" внутри асинхронного метода, в какой момент выполняется "продолжение" кода после await? Какой механизм отвечает за это - некая Polling-based система, или это всё же Event-based механизм?

### На чём тестируем?

В классе ContinuationExample.cs представлен незамысловатый пример асинхронного ожидания: асинхронный метод Test(), возвращающий Task, вызывается в асинхронном Program.cs.

````
public class Program
{
    public static async Task Main(string[] args)
    {
        await ContinuationExample.Test();
        Console.WriteLine("Hello, World!");
    }
}
````
Внутри Test() вызывается другой асинхронный метод: DoWorkAsync(). Там захватывается локальная переменная типа int, выполняется ожидание в 100 миллисекунд, после чего выполнение передаётся обратно вызывающему коду.

````
public static class ContinuationExample
{
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
}
````
### Разбор

Ещё до того, как выполнится первая строчка кода в рантайме, компилятор C# превращает класс Program в стейт машину. Было:

````
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
````
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

Разберём что здесь происходит, line-by-line.

Компилятор сгенерировал для метода класса Program стейт машину < Main >d__0. Это структура, реализующая интерфейс [IAsyncStateMachine](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/IAsyncStateMachine.cs):

````
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Represents state machines generated for asynchronous methods.
    /// This type is intended for compiler use only.
    /// </summary>
    public interface IAsyncStateMachine
    {
        /// <summary>Moves the state machine to its next state.</summary>
        void MoveNext();
        /// <summary>Configures the state machine with a heap-allocated replica.</summary>
        /// <param name="stateMachine">The heap-allocated replica.</param>
        void SetStateMachine(IAsyncStateMachine stateMachine);
    }
}
````

Внутри стейт машины, созданы три поля:
- `int` state
- `AsyncTaskMethodBuilder` builder
- `TaskAwaiter` u__1

В поле `<>t__builder` проставляется инстанс структуры [AsyncTaskMethodBuilderT](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs):
````
stateMachine.<>t__builder = AsyncTaskMethodBuilder.Create(); // декомпилированная Program.cs
...

public static AsyncTaskMethodBuilder<TResult> Create() => default; // AsyncTaskMethodBuilder<TResult>, CS: 27
````

Затем, у стейт машины состояние сразу выставляется в -1: этот стейт означает исполнение с начала метода.
````
stateMachine.<>1__state = -1;
````
Когда (если) исполнение дойдёт до ожидания первого await, стейт выставится в 0. До второго await - 1. До третьего await - 2, и так далее.
Стейт -2 будет означать что асинхронная стейт машина завершила работу.

Наконец, мы просим `<>t__builder` запустить стейт машину вызовом метода `Start(ref stateMachine)`, и возвращаем наверх Task.

Здесь интересен тот момент, что операционной системе и .NET нужна синхронная точка входа нашей программы, поэтому когда мы объявили `public static async Task Main(string[] args)`, он не стал истинной точкой входа. Компилятор сгенерировал "настоящую" точку входа с другой сигнатурой, которая синхронно вызывает наш `async Main`:
````
[SpecialName]
private static void <Main>([Nullable(1)] string[] args)
{
  Program.Main(args).GetAwaiter().GetResult();
}
````


[TaskAwaiter](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/TaskAwaiter.cs) это readonly структура, которая в конструкторе проверяет что ей не передали null вместо Task, и записывает task к себе в поля:
````
internal TaskAwaiter(Task task)
{
    Debug.Assert(task != null, "Constructing an awaiter requires a task to await.");
    m_task = task;
}
````

Наконец, у `AsyncTaskMethodBuilder` вызывается start. Через Builder вызов Start проксируется в [AsyncMethodBuilderCore](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncMethodBuilderCore.cs). Там происходит первый вызов MoveNext() стейт машины:
````
...
try
{
    stateMachine.MoveNext();
}
...
````

Внутри метода MoveNext() создаются локальные переменные num1 и num2: они являются compiler-generated optimization noise. Наличие данных переменных связано с тем, как генерируются IL-инструкции, а не с функциональным их использованием.

Далее мы проверяем что num1 (_туда мы только что скопировали state в локальный стек исполнения, т.к. IL инструкция чтения из стека значительно дешевле чтения переменной из полей структуры_) не равен нулю: 0 в контексте данного MoveNext() означал бы, что мы ожидаем первый await. Но т.к. мы его ещё не ожидаем, а находимся в стейте -1, мы попадаем в данную ветвь.

Внутри неё мы просим Task создать нам TaskAwaiter: `awaiter = ContinuationExample.Test().GetAwaiter();`. И проверяем, что он не Completed. 

В результате данного вызова `Test()` начинает работу синхронно. Там происходит такая же цепочка: создаётся стейт машина, создаётся Builder, у Builder вызывается Start(). Start() вызывает MoveNext(), и тот первым делом синхронно пишет:
````
Console.WriteLine("Start");
````
Вот так это выглядит в Low-Level C#:
````
        ...
        TaskAwaiter awaiter;
        int num2;
        if (num1 != 0)
        {
          Console.WriteLine("Start");
          awaiter = ContinuationExample.DoWorkAsync().GetAwaiter();
        ...
````
И дойдя до await, Test() тоже делает GetAwaiter(), который по цепочке синхронно вызывает уже DoWorkAsync():
````
public static async Task DoWorkAsync()
{
    var localVariable = 42;
    await Task.Delay(100);
    Console.WriteLine("Resumed with " + localVariable);
}
````
В low-level C# для метода DoWorkAsync() создаётся стейт машина, которая записывает в поля структуры локальную переменную: `this.<localVariable>5__2 = 42;`. Затем - просит TaskAwaiter:
````
...
awaiter = Task.Delay(10000).GetAwaiter();
...
````
Если бы awaiter оказался моментально завершён, наш код бы выполнился синхронно - мы сразу укажем стейт "завершён" и отдадим результат в Builder:
````
...
this.<>1__state = -2;
this.<>t__builder.SetResult();
...
````
Однако в примере с ожиданием awaiter не завершён, поэтому во всём нашем примере это будет первый вызов await, который не сможет завершиться синхронно, и мы наконец попадём в `AwaitUnsafeOnCompleted`:
````
[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine, [NotNull] ref Task<TResult>? taskField)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
{
    IAsyncStateMachineBox box = GetStateMachineBox(ref stateMachine, ref taskField);
    AwaitUnsafeOnCompleted(ref awaiter, box);
}
````
Можно заметить, что здесь создаётся AsyncStateMachineBox<TStateMachine>. Это класс, и его название не случайно. Он в буквальном смысле используется для boxing нашей стейт машины в управляемой куче, чтобы потом продолжить исполнение кода после await:
````
...
box.StateMachine = stateMachine; // AsyncTaskMethodBuilderT, CS: 225
box.Context = currentContext;
...
return box;
...
````

Через Builder, вызов AwaitUnsafeOnCompleted передаётся в TaskAwaiter:
````
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

Если Task уже Completed, мы сразу попросим ThreadPool выполнить continuation, как только появится свободный поток:
````
...
if (!AddTaskContinuation(stateMachineBox, addBeforeOthers: false)) // Task.cs, CS: 2592
{
    ThreadPool.UnsafeQueueUserWorkItemInternal(stateMachineBox, preferLocal: true);
}
...
````

Но если таск не завершён, мы попадаем в метод AddTaskContinuation: 
````
private bool AddTaskContinuation(object tc, bool addBeforeOthers) // Task.cs, CS: 4598
{
    Debug.Assert(tc != null);

    // Make sure that, if someone calls ContinueWith() right after waiting for the predecessor to complete,
    // we don't queue up a continuation.
    if (IsCompleted) return false;

    // Try to just jam tc into m_continuationObject
    if ((m_continuationObject != null) || (Interlocked.CompareExchange(ref m_continuationObject, tc, null) != null))
    {
        // If we get here, it means that we failed to CAS tc into m_continuationObject.
        // Therefore, we must go the more complicated route.
        return AddTaskContinuationComplex(tc, addBeforeOthers);
    }
    else return true;
}
````

Здесь вот этот вызов: `Interlocked.CompareExchange(ref m_continuationObject, tc, null)` присваивает объект класса AsyncStateMachineBox с нашей стейт машиной внутри, в переменную `m_continuationObject` внутри объекта Task.

Таким образом, AsyncStateMachineBox кладётся в управляемую кучу до тех пор, пока ОС и инфраструктура .NET не вызовут завершение таймера:
````
// стектрейс
Task.DelayPromise.CompleteTimedOut()
TimerQueueTimer.Fire()
TimerQueue.FireNextTimers()
ThreadPoolWorkQueue.Dispatch()
PortableThreadPool.WorkerThread.WorkerThreadStart()
````

В результате чего мы попадаем в метод CompleteTimedOut внутреннего вложенного класса Task - `DelayPromise`:
````
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

В методе `TrySetResult()` мы попадаем в метод `RunContinuations()`, внутри которого есть свич, который видит что continuationObject - это `AsyncStateMachineBox`, и вызывает `RunOrScheduleAction`.

И если мы попали в Happy path, там вызывается box.MoveNext():
````
...
try // TaskContinuation.cs, CS: 795
{
    if (prevCurrentTask != null) currentTask = null;
    box.MoveNext();
}
...
````

Что, наконец, приведёт нас к завершающей части стейт машины метода `DoWorkAsync()`:
````
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
Стейт снова выставится в -1, т.к. мы больше не ожидаем первый await. Ожидание завершится через awaiter.GetResult(), и мы выведем в консоль `"Resumed with 42"`.

Затем мы выставим состояние стейт машины `DoWorkAsync()` в -2 (_завершено_) и обратимся к `this.<>t__builder.SetResult();`. По цепочке вызовется FinishContinuations() для всех вызывающих стейт машин: методов Test() и Main().

Задача Main завершается. На этом этапе просыпается скрытая точка входа (static void <Main>), которая была заблокирована вызовом .GetAwaiter().GetResult(). Процесс завершается. (todo: вот тут поподробнее)

// что происходило в родительских стейт машинах, и как выполнится цепочка вызовов?