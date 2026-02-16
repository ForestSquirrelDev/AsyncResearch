В C#, один метод может выбросить только одно исключение. Если написать вот так:
````csharp
...
try
{
    throw new Exception("HORY SHIET!");
}
catch
{
}
finally
{
    throw new Exception("Oh no!");
}
...
````
Будет выброшено только последнее исключение.

Это работает и с конструкцией `await using`. Возьмём пример:
````csharp
public static async Task RunAwaitUsingExample()
{
    try
    {
        await using (var resource = new AsyncResource())
        {
            resource.DoWork();
        } // <--- Здесь неявно вызывается await resource.DisposeAsync()
    }
    catch (Exception ex)
    {
        // Если и в try, и в DisposeAsync будут ошибки, 
        // здесь мы увидим ТОЛЬКО ту, что из DisposeAsync.
        Console.WriteLine($"Поймали исключение: {ex.Message}");
    }
}

private class AsyncResource : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Console.WriteLine("--- Начинаем DisposeAsync... ---");
        await Task.Delay(100); // Имитация асинхронной работы (например, закрытие сокета)
        throw new Exception("Исключение в DisposeAsync (Cleanup Error)");
    }

    public void DoWork() => throw new Exception("Original Exception (Ошибка логики)");
}
````

Здесь мы обращаемся к некоему ресурсу, который по завершении работы необходимо высвободить асинхронно. Внутри метода "работы", мы выбрасываем исключение. И в `DisposeAsync()` - тоже.
Вывод программы:
````
--- Начинаем DisposeAsync... ---
Поймали исключение: Исключение в DisposeAsync (Cleanup Error)

Process finished with exit code 0.
````

В декомпилированном виде это развернётся в такую логику:
````csharp
...
// 1. Сначала мы через try-catch выполняем "работу"
try
{
    // Здесь ресурс выбросит исключение
    resource.DoWork();
}
catch (object ex)
{
    // Сохранили исключение в поля стейт машины
    this.<>7__wrap1 = ex;
}
...
// 2. Дальше мы берём Awaiter у DisposeAsync(), и ожидаем его завершения
if (resource != null)
{
  awaiter = resource.DisposeAsync().GetAwaiter();
  if (!awaiter.IsCompleted)
  {
    this.<>1__state = num2 = 0;
    this.<>u__1 = awaiter;
    this.<>t__builder.AwaitUnsafeOnCompleted<ValueTaskAwaiter, AwaitUsingExample.<RunAwaitUsingExample>d__0>(ref awaiter, ref this);
    return;
  }
}
// 3. После того как мы вернёмся в MoveNext(), мы вызовем:
...
awaiter.GetResult();
...
// 4. И упадём. Исключение останется навсегда лежать в стейт машине, и его никто не заберёт. Даже UnobservedTaskException не сработает, потому что исключение не в таске, а просто
// в полях стейт машины:
label_11:
  // Сюда мы уже не попадём
  object obj = this.<>7__wrap1;
  if (obj != null)
  {
    if (!(obj is Exception source))
      throw obj;
    ExceptionDispatchInfo.Capture(source).Throw();
  }
  this.<>7__wrap1 = (object) null;
````

Это не так очевидно как обычный `try-catch` из-за синтаксического сахара `await using`. К тому же, если `Dispose()` асинхронный, то вероятность упасть в исключение там по умолчанию выше:
если это запрос к сети - у нас могла отвалиться локальная сеть; если запрос к shared ресурсу - на него могли взять lock, и т.п.

Избежать сокрытия исключения можно через явный `try-catch`:
````csharp
var resource = new AsyncResource();
try
{
    resource.DoWork();
}
catch (Exception ex)
{
    Console.WriteLine($"Поймали исключение: {ex.Message}");
    try
    {
        await resource.DisposeAsync();
    }
    catch (Exception disposeEx)
    {
        throw new AggregateException(ex, disposeEx);
    }
}
````

Вывод программы:
````
Поймали исключение: Original Exception (Ошибка логики)
--- Начинаем DisposeAsync... ---
Unhandled exception. System.AggregateException: One or more errors occurred. (Original Exception (Ошибка логики)) (Исключение в DisposeAsync (Cleanup Error))
 ---> System.Exception: Original Exception (Ошибка логики)
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AwaitUsingAggregateExceptionExample.AsyncResource.DoWork()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AwaitUsingAggregateExceptionExample.RunAwaitUsingExample()
   --- End of inner exception stack trace ---
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AwaitUsingAggregateExceptionExample.RunAwaitUsingExample()
   at AsyncResearch.Program.Main(String[] args) in D:\work\AsyncResearch\Program.cs:line 9
   at AsyncResearch.Program.<Main>(String[] args)
 ---> (Inner Exception #1) System.Exception: Исключение в DisposeAsync (Cleanup Error)
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AwaitUsingAggregateExceptionExample.AsyncResource.DisposeAsync()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AwaitUsingAggregateExceptionExample.RunAwaitUsingExample()

Process finished with exit code -532,462,766.
````