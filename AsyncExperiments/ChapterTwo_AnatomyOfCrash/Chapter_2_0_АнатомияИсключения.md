### Внутри асинхронной стейт машины было выброшено необработанное исключение. Как оно попадёт в точку входа, если вся цепочка методов возвращает Task? 

Возьмём пример: асинхронный метод `Main()` вызывает два других асинхронных метода по цепочке. Все возвращают `Task`, и последний (`DoWorkAsync()`) 
внутри себя обращается к системному таймеру и создаёт `DelayPromise`, что приводит к упаковке стейт машины в управляемую кучу.
Во втором асинхронном методе, после возвращения из ожидания системного таймера, выбрасывается исключение.

````
public static async Task Main(string[] args)
{
    await AsyncTryCatchExample.Test();
    Console.WriteLine("Hello, World!");
}

public static async Task Test()
{
    Console.WriteLine("Start");
    await DoWorkAsync();
    throw new Exception("HORY SHET!");
}

public static async Task DoWorkAsync()
{
    var localVariable = 42;
    await Task.Delay(10000);
    Console.WriteLine("Resumed with " + localVariable);
}
````
**Разбор**

Стейт машина, генерируясь компилятором, оборачивает весь свой код в try-catch:

````
void IAsyncStateMachine.MoveNext()
{
  int num1 = this.<>1__state;
  try
  {
    ...
  }
  catch (Exception ex)
  {
    this.<>1__state = -2;
    this.<>t__builder.SetException(ex);
  }
}
````

Отлавливая исключение, стейт машина проставляет себе состояние "завершено" (-2) и вызывает `<>t__builder.SetException(ex);`.
`AsyncTaskMethodBuilderT` передаёт исключение в Task. Task выставляет себе стейт Faulted, затем Lazy инициализирует свой внутренний объект m_exceptionsHolder, хранящий исключения, 
и добавляет наш `Exception("HORY SHET!")`туда:
````
EnsureContingentPropertiesInitialized(); // Task.cs, CS: 3386
if (AtomicStateUpdate(
    (int)TaskStateFlags.CompletionReserved,
    (int)TaskStateFlags.CompletionReserved | (int)TaskStateFlags.RanToCompletion | (int)TaskStateFlags.Faulted | (int)TaskStateFlags.Canceled))
{
    AddException(exceptionObject); // handles singleton exception or exception collection
    Finish(false);
    returnValue = true;
}
...
ContingentProperties props = EnsureContingentPropertiesInitialized(); // Task.cs, CS: 1770
if (props.m_exceptionsHolder == null)
{
    TaskExceptionHolder holder = new TaskExceptionHolder(this);
    if (Interlocked.CompareExchange(ref props.m_exceptionsHolder, holder, null) != null)
    {
        // If someone else already set the value, suppress finalization.
        holder.MarkAsHandled(false);
    }
}

lock (props)
{
    props.m_exceptionsHolder.Add(exceptionObject, representsCancellation);
}
````

Там хранится список исключений, а не одно исключение, поскольку несколько "дочерних" тасков могли пробросить исключения по цепочке наверх - родительским стейт машинам.

Когда стейт машина метода `Test()` выбросит исключение, его поймает стейт машина "ложной" точки входа `Main()` через try-catch. Произошло что-то вроде цепочки или эстафеты исключений.
Из-за try-catch, метод `Main()` тоже просто запишет к себе в m_exceptionsHolder данное исключение, пометит состояние Task как Faulted, и вернёт этот Faulted Task наверх.
Так будет происходить до тех пор, пока мы не дойдём до места, в котором try-catch отсутствует и исключение не обрабатывается. Это место - наша истинная точка входа:
````
[SpecialName]
private static void <Main>([Nullable(1)] string[] args)
{
    Program.Main(args).GetAwaiter().GetResult();
}
````
Блокирующий вызов GetResult() приведёт к вызову метода ValidateEnd:
````
[StackTraceHidden]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static void ValidateEnd(Task task, ConfigureAwaitOptions options = ConfigureAwaitOptions.None) // TaskAwaiter.cs, CS: 79
{
    // Fast checks that can be inlined.
    if (task.IsWaitNotificationEnabledOrNotRanToCompletion)
    {
        // If either the end await bit is set or we're not completed successfully,
        // fall back to the slower path.
        HandleNonSuccessAndDebuggerNotification(task, options);
    }
}
````
И там, видя, что Task завершился неуспехом, TaskAwaiter выбрасывает исключение `Exception("HORY SHET!")`:
````
if (!task.IsCompletedSuccessfully) // TaskAwaiter.cs, CS: 114   
{
    if ((options & ConfigureAwaitOptions.SuppressThrowing) == 0)
    {
        ThrowForNonSuccess(task);
    }

    task.MarkExceptionsAsHandled();
}
...
case TaskStatus.Faulted: // TaskAwaiter.cs, CS: 150
    List<ExceptionDispatchInfo> edis = task.GetExceptionDispatchInfos();
    if (edis.Count > 0)
    {
        edis[0].Throw();
        Debug.Fail("Throw() should have thrown");
        break; // Necessary to compile: non-reachable, but compiler can't determine that
    }
    else
    {
        Debug.Fail("There should be exceptions if we're Faulted.");
        throw task.Exception!;
    }
````

И поскольку в истинной точке входа уже нету try-catch, которые были в стейт машинах, исключение выбрасывается на уровне процесса, и процесс завершается:
````
Start
Resumed with 42
Unhandled exception. System.Exception: HORY SHET!
   at AsyncResearch.AsyncExperiments.ChapterTwo_AnatomyOfCrash.AsyncTryCatchExample.Test() in D:\work\AsyncResearch\AsyncExperiments\ChapterTwo_AnatomyOfCrash\AsyncTryCatchExample.cs:line 9
   at AsyncResearch.Program.Main(String[] args) in D:\work\AsyncResearch\Program.cs:line 10
   at AsyncResearch.Program.<Main>(String[] args)
````

