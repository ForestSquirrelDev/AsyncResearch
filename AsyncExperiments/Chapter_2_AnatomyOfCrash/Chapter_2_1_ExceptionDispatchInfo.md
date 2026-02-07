Разработчики CLR разделили механику проброса исключения на три сценария:

- `throw;` Сохраняет оригинал и добавляет текущую точку вызова. Только внутри блока catch, иначе - ошибка компиляции.
- `throw ex;` Перезаписывает всё. Старая история стирается.	Когда нужно стереть предыдущий стактрейс и выдать ошибку от места вызова.
- `ExceptionDispatchInfo.Throw(ex);`, или `ExceptionDispatchInfo.Capture(ex).Throw()`. Восстанавливает оригинал из «снимка» и продолжает его. 

#### Поговорим об ExceptionDispatchInfo

Чтобы стактрейс не превратился в тыкву, когда он выбрасывается через упакованный в управляемую кучу `MoveNext()`, рантайм .NET использует объект
`ExceptionDispatchInfo` (EDI).

В примере TaskUnhandledExceptionExample стейт машина Test() выбрасывает исключение сразу после того, как у неё вызовется MoveNext по завершении DelayPromise в стейт машине DoWorkAsync:
````
public static async Task Main(string[] args)
{
    await TaskUnhandledExceptionExample.Test();
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

При этом Test() отловит исключение, которое он сам же и выбросил:
````
private struct <Test>d__0 : 
...
    void IAsyncStateMachine.MoveNext()
      {
        int num1 = this.<>1__state;
        try
        {
            ...
        catch (Exception ex)
        {
          this.<>1__state = -2;
          this.<>t__builder.SetException(ex);
        }
...
````
И затем передаст это исключение в `AsyncTaskMethodBuilderT`. Здесь происходит самое важное: SetException не только записывает к себе `Exception` object, но и захватывает трейс:
````
private void AddFaultException(object exceptionObject) // TaskExceptionHolder.cs, CS: 143
{
    Debug.Assert(exceptionObject != null, "AddFaultException(): Expected a non-null exceptionObject");

    // Initialize the exceptions list if necessary.  The list should be non-null iff it contains exceptions.
    List<ExceptionDispatchInfo>? exceptions = m_faultExceptions ??= new List<ExceptionDispatchInfo>(1);

    // Handle Exception by capturing it into an ExceptionDispatchInfo and storing that
    if (exceptionObject is Exception exception)
    {
        // Вот тут мы захватили трейс
        exceptions.Add(ExceptionDispatchInfo.Capture(exception));
    }
    else
    {
        // Если исключение уже и так ExceptionDispatchInfo - значит просто добавляем в список исключений
        
        // Handle ExceptionDispatchInfo by storing it into the list
        if (exceptionObject is ExceptionDispatchInfo edi)
        {
            exceptions.Add(edi);
        }
        else
        {
            // Handle enumerables of exceptions by capturing each of the contained exceptions into an EDI and storing it
            if (exceptionObject is IEnumerable<Exception> exColl)
            {
#if DEBUG
                int numExceptions = 0;
#endif
                foreach (Exception exc in exColl)
                {
#if DEBUG
                    Debug.Assert(exc != null, "No exceptions should be null");
                    numExceptions++;
#endif
                    exceptions.Add(ExceptionDispatchInfo.Capture(exc));
                }
#if DEBUG
                Debug.Assert(numExceptions > 0, "Collection should contain at least one exception.");
#endif
            }
            else
            {
                // Handle enumerables of EDIs by storing them directly
                if (exceptionObject is IEnumerable<ExceptionDispatchInfo> ediColl)
                {
                    exceptions.AddRange(ediColl);
#if DEBUG
                    Debug.Assert(exceptions.Count > 0, "There should be at least one dispatch info.");
                    foreach (ExceptionDispatchInfo tmp in exceptions)
                    {
                        Debug.Assert(tmp != null, "No dispatch infos should be null");
                    }
#endif
                }
                // Anything else is a programming error
                else
                {
                    throw new ArgumentException(SR.TaskExceptionHolder_UnknownExceptionType, nameof(exceptionObject));
                }
            }
        }
    }

    if (exceptions.Count > 0)
        MarkAsUnhandled();
}
...
internal DispatchState CaptureDispatchState() // Exception.CoreCLR.cs, CS: 251
{
    GetStackTracesDeepCopy(this, out byte[]? stackTrace, out object[]? dynamicMethods);

    return new DispatchState(stackTrace, dynamicMethods,
        _remoteStackTraceString, _ipForWatsonBuckets, _watsonBuckets);
}
````
То есть в `Task` стейт машины `Test()` в итоге окажется исключение со стек трейсом метода `Test()`.

Когда у стейт машины `Main()` вызовут `MoveNext()`, `GetResult()` выбросит сохранённое исключение, и стейт машина `Main()` запишет его уже в свою таску,
перед этим "дописав" себя в этот стактрейс. Это возможно за счёт того, что `ExceptionDispatchInfo.Throw()` сначала восстанавливает оригинальный стактрейс, а потом выбрасывает исключение. Затем в `SetResult()` стейт машины `Main()` произойдёт то же самое,
что ранее произошло в стейт машине `Test()`: `Main()` допишет кусочек себя через захват в массив байтов, стак трейс вырастет, и продолжит путешествовать и разрастаться как DeepCopy в виде массива байтов до тех пор, 
пока исключение не будет выброшено (или проигнорировано и собрано GC):
````
public void Throw() // ExceptionDispatchInfo.cs, CS: 49
{
    // Restore the exception dispatch details before throwing the exception.
    _exception.RestoreDispatchState(_dispatchState);
    throw _exception;
}
````

Наконец, истинная точка входа Main(), вызвав GetResult() без try-catch, выбросит исключение. Но GetResult() делает это не просто через throw, а с помощью EDI:
````
[SpecialName]
private static void <Main>([Nullable(1)] string[] args)
{
  Program.Main(args).GetAwaiter().GetResult();
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
````

Exception учитывает оригинальный стактрейс, захваченный EDI, и восстанавливает его:
````
internal void RestoreDispatchState(in DispatchState dispatchState) // Task.cs, CS: 141
{
    // Restore only for non-preallocated exceptions
    if (!IsImmutableAgileException(this))
    {
        // When restoring back the fields, we again create a copy and set reference to them
        // in the exception object. This will ensure that when this exception is thrown and these
        // fields are modified, then EDI's references remain intact.
        //
        byte[]? stackTraceCopy = (byte[]?)dispatchState.StackTrace?.Clone();
        object[]? dynamicMethodsCopy = (object[]?)dispatchState.DynamicMethods?.Clone();

        // Watson buckets and remoteStackTraceString fields are captured and restored without any locks. It is possible for them to
        // get out of sync without violating overall integrity of the system.
        _watsonBuckets = dispatchState.WatsonBuckets;
        _ipForWatsonBuckets = dispatchState.IpForWatsonBuckets;
        _remoteStackTraceString = dispatchState.RemoteStackTrace;

        // The binary stack trace and references to dynamic methods have to be restored under a lock to guarantee integrity of the system.
        SaveStackTracesFromDeepCopy(this, stackTraceCopy, dynamicMethodsCopy);

        _stackTraceString = null;

        // Marks the TES state to indicate we have restored foreign exception
        // dispatch information.
        PrepareForForeignExceptionRaise();
    }
}
````

В результате исполнения программы, мы увидим место, где на самом деле произошло исключение, несмотря на то что оно было выброшено в другом месте:
````
Unhandled exception. System.Exception: HORY SHET!
   at AsyncResearch.AsyncExperiments.Chapter_2_AnatomyOfCrash.TaskUnhandledExceptionExample.Test()
   at AsyncResearch.Program.Main(String[] args)
   at AsyncResearch.Program.<Main>(String[] args)
````

А если бы мы сделали просто `throw exception`, CLR посчитал бы, что исключение возникло прямо сейчас. Весь предыдущий путь исключения был бы стёрт и замененм текущим местом.