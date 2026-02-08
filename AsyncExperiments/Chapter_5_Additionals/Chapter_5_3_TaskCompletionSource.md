Существует способ вызвать stateMachine.MoveNext() вручную: для этого нужно привязать её выполнение к TaskCompletionSource.

````csharp
public static async Task Test()
{
    var tcs = new TaskCompletionSource();
    var task = DoWorkAsync(tcs);
    tcs.SetResult();
    await task;
}

private static async Task DoWorkAsync(TaskCompletionSource tcs)
{
    Console.WriteLine("DoWorkAsync: before await");
    await tcs.Task;
    Console.WriteLine($"DoWorkAsync: after await, stack trace {new StackTrace()}");
}
````

Результатом выполнения программы станет следующий вывод:
````
DoWorkAsync: before await
DoWorkAsync: after await, stack trace    at AsyncResearch.AsyncExperiments.Chapter_5_Additionals.TaskCompletionSourceExample.DoWorkAsync(TaskCompletionSource tcs)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.ExecutionContextCallback(Object s)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext(Thread threadPoolThread)
   at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.AsyncStateMachineBox`1.MoveNext()
   at System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox box, Boolean allowInlining)
   at System.Threading.Tasks.Task.RunContinuations(Object continuationObject)
   at System.Threading.Tasks.Task.TrySetResult()
   at System.Threading.Tasks.TaskCompletionSource.TrySetResult()
   at System.Threading.Tasks.TaskCompletionSource.SetResult()
   at AsyncResearch.AsyncExperiments.Chapter_5_Additionals.TaskCompletionSourceExample.Test()
   at System.Runtime.CompilerServices.AsyncMethodBuilderCore.Start[TStateMachine](TStateMachine& stateMachine)
   at AsyncResearch.AsyncExperiments.Chapter_5_Additionals.TaskCompletionSourceExample.Test()
   at AsyncResearch.Program.Main(String[] args)
   at System.Runtime.CompilerServices.AsyncMethodBuilderCore.Start[TStateMachine](TStateMachine& stateMachine)
   at AsyncResearch.Program.Main(String[] args)
   at AsyncResearch.Program.<Main>(String[] args)


Process finished with exit code 0.
````

По стактрейсу можно видеть, что мы вызвали `MoveNext()` стейт машины метода `DoWorkAsync` вызовом `tcs.SetResult()`.

Так можно привязывать стейт машину к своим кастомным задачам, или, например, гарантировать `вызов MoveNext()` из основного потока: в случае `TaskCompletionSource` мы сами управляем тем,
из какого потока вызовется `RunContinuations`, а не `CLR` и `ThreadPool`.