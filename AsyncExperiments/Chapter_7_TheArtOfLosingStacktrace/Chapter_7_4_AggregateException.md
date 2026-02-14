Возьмём пример: 
````csharp
public static void AggregateExceptionTest()
{
    try
    {
        Layer0().Wait();
    }
    catch (Exception ex) // Тип: AggregateException
    {
        Console.WriteLine(ex.StackTrace);
    }
}

private static async Task Layer0()
{
    await Task.Delay(100);
    await Layer1();
}

private static async Task Layer1()
{
    await Task.Delay(100);
    await Layer2();
}

private static async Task Layer2()
{
    await Task.Delay(100);
    await Layer3();
}

private static async Task Layer3()
{
    await Task.Delay(100);
    await Layer4();
}

private static async Task Layer4()
{
    await Task.Delay(100);
    throw new Exception("HORY SHIET!");
}
````

Рантайм .NET даёт нам возможность ожидать результата `Task` блокирующим вызовом, через `Spin Wait`. Когда мы так делаем, список исключений оборачивается в `AggregateException`:
````csharp
internal AggregateException CreateExceptionObject(bool calledFromFinalizer, Exception? includeThisException) // TaskExceptionHolder.cs, CS: 252
{
    List<ExceptionDispatchInfo>? exceptions = m_faultExceptions;
    Debug.Assert(exceptions != null, "Expected an initialized list.");
    Debug.Assert(exceptions.Count > 0, "Expected at least one exception.");

    MarkAsHandled(calledFromFinalizer);
    
    if (includeThisException == null)
        return new AggregateException(exceptions);
    
    Exception[] combinedExceptions = new Exception[exceptions.Count + 1];
    for (int i = 0; i < combinedExceptions.Length - 1; i++)
    {
        combinedExceptions[i] = exceptions[i].SourceException;
    }
    combinedExceptions[^1] = includeThisException;
    return new AggregateException(combinedExceptions);
}
````

И в нашем `try-catch` в методе `AggregateExceptionTest()` окажется именно `AggregateException()`. Если написать вот так:
````chsarp
...
try
{
    Layer0().Wait();
}
catch (Exception ex)
{
    Console.WriteLine(ex.StackTrace);
}
...
````

Вывод программы будет следующим:
````csharp
   at System.Threading.Tasks.Task.ThrowIfExceptional(Boolean includeTaskCanceledExceptions)
   at System.Threading.Tasks.Task.Wait(Int32 millisecondsTimeout, CancellationToken cancellationToken)
   at System.Threading.Tasks.Task.Wait()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AggregateExceptionExample.AggregateExceptionTest()
````

Весь путь исключения, в том числе место где оно реально выбросилось - `Layer4()`, потерялся. Так происходит потому, что метод `ThrowIfExceptional()` делает простой `throw exception`:
````csharp
internal void ThrowIfExceptional(bool includeTaskCanceledExceptions) // Task.cs, CS: 1887
{
    Debug.Assert(IsCompleted, "ThrowIfExceptional(): Expected IsCompleted == true");

    Exception? exception = GetExceptions(includeTaskCanceledExceptions);
    if (exception != null)
    {
        UpdateExceptionObservedStatus();
        throw exception;
    }
}
````

А при таком синтаксисе, CLR посчитает что исключение было выброшено именно здесь - и запишет место выброса в свойство `exception.Stacktrace`.

Чтобы сохранить оригинальный путь исключения в сценарии со Spin Wait, нужно отловить именно `AggregateException`, и обратиться к `InnerException`, либо вызвать `Flatten()`:
````csharp
...
try
{
    Layer0().Wait();
}
catch (AggregateException ex)
{
    Console.WriteLine(ex.Flatten());
}
...
````

Вывод программы:
````
System.AggregateException: One or more errors occurred. (HORY SHIET!)
 ---> System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AggregateExceptionExample.Layer4()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AggregateExceptionExample.Layer3()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AggregateExceptionExample.Layer2()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AggregateExceptionExample.Layer1()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AggregateExceptionExample.Layer0()
   --- End of inner exception stack trace ---
````