Ещё один хитрый способ потерять одно или несколько исключений - это `Task.WhenAll(...)`. Возьмём пример:
````csharp
public static async Task WhenAllExceptionsExample()
{
    var t1 = Exception1();
    var t2 = Exception2();
    var t3 = Exception3();
    
    await Task.WhenAll(t1, t2, t3);
}

private static async Task Exception1()
{
    throw new Exception("Exception1");
}

private static async Task Exception2()
{
    throw new Exception("Exception2");
}

private static async Task Exception3()
{
    throw new Exception("Exception3");
}
````

В методах, выбрасывающих исключения, нет асихронного ожидания. Мы наверняка знаем, что на момент вызова `t1`, `t2` и `t3` исключение уже было выброшено и хранится внутри тасок. 
Кажется логичным, что ожидание всех тасок с исключением, вернёт нам все три исключения. Но вопреки ожиданиями, вывод будет следующим:
````
Unhandled exception. System.Exception: Exception1
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllExceptionsExample.Exception1()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllExceptionsExample.WhenAllExceptionsExample()
   at AsyncResearch.Program.Main(String[] args)
   at AsyncResearch.Program.<Main>(String[] args)

Process finished with exit code -532,462,766.
````

Так произошло, потому что `TaskAwaiter` берёт и буквально выбрасывает только первое исключение из списка:
````csharp
  private static void ThrowForNonSuccess(Task task)
  {
    switch (task.Status)
    {
      case TaskStatus.Canceled:
        task.GetCancellationExceptionDispatchInfo()?.Throw();
        throw new TaskCanceledException(task);
      case TaskStatus.Faulted:
        ReadOnlyCollection<ExceptionDispatchInfo> exceptionDispatchInfos = task.GetExceptionDispatchInfos();
        if (exceptionDispatchInfos.Count <= 0)
          throw task.Exception;
        // Выбросили только Exception1
        exceptionDispatchInfos[0].Throw();
        break;
    }
  }
````

Этого можно избежать двумя способами. Первый - вручную достать все исключения из `Task`:
````csharp
public static async Task WhenAllExceptionsExample()
{
    var t1 = Exception1();
    var t2 = Exception2();
    var t3 = Exception3();
    
    var allTasks = Task.WhenAll(t1, t2, t3);
    try
    {
        await allTasks;
    }
    catch
    {
        var realExceptions = allTasks.Exception.InnerExceptions;
        for (var i = 0; i < realExceptions.Count; i++)
        {
            var exception = realExceptions[i];
            Console.WriteLine($"\nИсключение номер {i + 1}: {exception}");
        }
    }
}
````

Вывод:
````
Исключение номер 1: System.Exception: Exception1
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllForeachExample.Exception1()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllForeachExample.WhenAllExceptionsExample()

Исключение номер 2: System.Exception: Exception2
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllForeachExample.Exception2()

Исключение номер 3: System.Exception: Exception3
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllForeachExample.Exception3()

Process finished with exit code 0.
````

Второй способ - блокирующий `Wait()` у Task. Его вызов создаст для нас `AggregateException`:
````csharp
public static async Task WhenAllExceptionsExample()
{
    var t1 = Exception1();
    var t2 = Exception2();
    var t3 = Exception3();
    
    var allTasks = Task.WhenAll(t1, t2, t3);
    try
    {
        await allTasks;
    }
    catch
    {
        // Ничего не делаем с исключениями, чтобы дальше вызвать Wait()
    }

    allTasks.Wait();
}
````

Вывод:
````
Unhandled exception. System.AggregateException: One or more errors occurred. (Exception1) (Exception2) (Exception3)
 ---> System.Exception: Exception1
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllSynchronousWaitExample.Exception1()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllSynchronousWaitExample.WhenAllExceptionsExample()
   --- End of inner exception stack trace ---
   at System.Threading.Tasks.Task.ThrowIfExceptional(Boolean includeTaskCanceledExceptions)
   at System.Threading.Tasks.Task.Wait(Int32 millisecondsTimeout, CancellationToken cancellationToken)
   at System.Threading.Tasks.Task.Wait()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllSynchronousWaitExample.WhenAllExceptionsExample()
   at AsyncResearch.Program.Main(String[] args)
   at AsyncResearch.Program.<Main>(String[] args)
 ---> (Inner Exception #1) System.Exception: Exception2
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllSynchronousWaitExample.Exception2()

 ---> (Inner Exception #2) System.Exception: Exception3
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAllSynchronousWaitExample.Exception3()


Process finished with exit code -532,462,766.
````