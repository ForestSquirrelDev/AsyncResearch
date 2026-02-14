Возьмём пример:

````csharp
public static async Task AsyncVoidExceptionTest()
{
    TaskCompletionSource tcs = new TaskCompletionSource();
    _ = Layer0(tcs);
    await tcs.Task;
}

private static async Task Layer0(TaskCompletionSource tcs)
{
    await Task.Delay(100);
    await Layer1(tcs);
}

private static async Task Layer1(TaskCompletionSource tcs)
{
    await Task.Delay(100);
    await Layer2(tcs);
}

private static async Task Layer2(TaskCompletionSource tcs)
{
    await Task.Delay(100);
    await Layer3(tcs);
}

private static async Task Layer3(TaskCompletionSource tcs)
{
    await Task.Delay(100);
    Layer4(tcs);
}

private static async void Layer4(TaskCompletionSource tcs)
{
    await Task.Delay(100);
    tcs.SetException(new Exception("HORY SHIET!"));
}
````

С первого взгляда может показаться, что при вызове `SetException(...)` у `TaskCompletionSource`, оригинальный стактрейс должен сохраниться - ведь мы передали `tcs` от самого верха.

Но здесь сработает тот же принцип, что и в разделе 7_0: на момент выброса исключения - в стактрейсе методов `Layer3()`, `Layer2()`, `Layer1()`, и `Layer0()` уже нету. Они лишь доставили
tcs в метод `Layer4()`, но никто из них не делал `await` таска у `tcs`. Первым, кто ожидает исключение - будет `AsyncVoidExceptionTest()`:
````
Unhandled exception. System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AsyncVoidTcsException.AsyncVoidExceptionTest()
   at AsyncResearch.Program.Main(String[] args)
   at AsyncResearch.Program.<Main>(String[] args)

Process finished with exit code -532,462,766.
````