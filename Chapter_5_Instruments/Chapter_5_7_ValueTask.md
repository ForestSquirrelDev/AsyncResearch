Вместо `Task`, в методе можно вернуть `ValueTask`:
````csharp
public static async ValueTask RunStateMachineValueTaskExample()
{
    await Task.Delay(100);
    Console.WriteLine("Hello, World!");
}
````

В декомпилированном виде, стейт машина и `builder` очень напоминают `async void`. В async методе возвращается `Task`:
````csharp
  [AsyncStateMachine(typeof (StateMachineValueTaskExample.<RunStateMachineValueTaskExample>d__0))]
  public static ValueTask RunStateMachineValueTaskExample()
  {
    StateMachineValueTaskExample.<RunStateMachineValueTaskExample>d__0 stateMachine;
    stateMachine.<>t__builder = AsyncValueTaskMethodBuilder.Create();
    stateMachine.<>1__state = -1;
    stateMachine.<>t__builder.Start<StateMachineValueTaskExample.<RunStateMachineValueTaskExample>d__0>(ref stateMachine);
    return stateMachine.<>t__builder.Task;
  }
````
А внутри стейт машины используется `AsyncValueTaskMethodBuilder`:
````csharp
public AsyncValueTaskMethodBuilder <>t__builder;
````

Как и в случае с `AsyncVoidMethodBuilder`, у `AsyncValueTaskMethodBuilder` внутри создаётся экземпляр `Task`:
````csharp
    [StructLayout(LayoutKind.Auto)]
    public struct AsyncValueTaskMethodBuilder // AsyncValueTaskMethodBuilder.cs, CS: 11
    {
        private static readonly Task<VoidTaskResult> s_syncSuccessSentinel = AsyncValueTaskMethodBuilder<VoidTaskResult>.s_syncSuccessSentinel; // AsyncValueTaskMethodBuilder.cs, CS: 14
        ...
        public ValueTask Task AsyncValueTaskMethodBuilder.cs, CS: 57
        {
            get
            {
                if (m_task == s_syncSuccessSentinel)
                {
                    return default;
                }

                Task<VoidTaskResult>? task = m_task ??= new Task<VoidTaskResult>();
                return new ValueTask(task);
            }
        }
        ...
    }
````

Как и у `AsyncTaskMethodBuilder`, в `AsyncValueTaskMethodBuilder` есть приятная оптимизация. Представим что наш асинхронный метод часто выполняется синхронно:
````csharp
public static async ValueTask RunStateMachineValueTaskExample()
{
    Console.WriteLine("Hello, World!");
}
````
Тогда сразу после вызова `builder.Start()`, мы попадём в `SetResult`, где `AsyncValueTaskMethodBuilder` выставит себе `s_syncSuccessSentinel`:
````csharp
public void SetResult()
{
    if (m_task is null)
    {
        m_task = s_syncSuccessSentinel;
    }
    else
    {
        AsyncTaskMethodBuilder<VoidTaskResult>.SetExistingTaskResult(m_task, default);
    }
}
````
Это произойдёт ещё до того, как метод `RunStateMachineValueTaskExample` вернёт `Task`. Если бы это был обычный `AsyncTaskMethodBuilder`, на возвращении `Task` в любом случае произошла бы
аллокация, даже если мы ничего не ждём, т.к. `Task` - это классик. Здесь же мы можем избежать аллокации, возвращая статический экземпляр таски. Такая же логика есть и в `AsyncTaskMethodBuilder`. 

Но в этом не основная фишка `RunStateMachineValueTaskExample`. Его главный use-case кроется в возвращении результата. Возьмём бенчмарк двух методов:
````csharp
private readonly int _cachedValue = 12345;

[Benchmark]
public Task<int> GetValueViaTask()
{
    return Task.FromResult(_cachedValue);
}

[Benchmark]
public ValueTask<int> GetValueViaValueTask()
{
    return new ValueTask<int>(_cachedValue);
}
````
Здесь оба метода без стейт машины возвращают какое-то значение. Таску для этого нужно создать классик и положиться в управляемую кучу. `ValueTask` же - это структура, он аллоцирует себя
на стеке, что позволяет нам избежать аллокаций. Результат бенчмарка:
````
| Method                         | Mean        | Error     | StdDev    | Median      | Gen0   | Allocated |
|------------------------------- |------------:|----------:|----------:|------------:|-------:|----------:|
| GetValueViaTask                |   7.8669 ns | 0.3183 ns | 0.9386 ns |   7.5419 ns | 0.0086 |      72 B |
| GetValueViaValueTask           |   1.7986 ns | 0.0176 ns | 0.0156 ns |   1.7962 ns |      - |         - |
````

`GetValueViaValueTask` не только аллоцировал 0 байт, но и выполнился в несколько раз быстрее.

Если же в методе происходит ожидание:
````csharp
[Benchmark]
public async Task TaskStateMachineWithYield()
{
    await Task.Yield();
}

[Benchmark]
public async ValueTask ValueTaskStateMachineWithYield()
{
    await Task.Yield();
}
````
То результат - одинаковый по аллокациям, и хуже чем `Task` по времени CPU:
````
| Method                         | Mean        | Error     | StdDev    | Median      | Gen0   | Allocated |
|------------------------------- |------------:|----------:|----------:|------------:|-------:|----------:|
| TaskStateMachineWithYield      | 619.8856 ns | 3.8579 ns | 3.6086 ns | 620.5922 ns | 0.0114 |      96 B |
| ValueTaskStateMachineWithYield | 633.3198 ns | 4.8983 ns | 4.5819 ns | 633.6582 ns | 0.0114 |      96 B |
````

Получается, `ValueTask` нет смысла использовать в местах, где наверняка или в большинстве случаев будет происходить ожидание - тогда `AsyncValueTaskMethodBuilder` всё равно придётся создать `Task`:
````csharp
// Тут внутри создастся AsyncStateMachineBox
public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) // AsyncValueTaskMethodBuilder.cs, CS: 84
    where TAwaiter : INotifyCompletion
    where TStateMachine : IAsyncStateMachine =>
    AsyncTaskMethodBuilder<VoidTaskResult>.AwaitOnCompleted(ref awaiter, ref stateMachine, ref m_task);
````

Нет смысла использовать и там, где метод часто завершается синхронно, но не возвращает никакой результат:
````csharp
[Benchmark]
public async Task TaskStateMachine()
{
}
        
[Benchmark]
public async ValueTask ValueTaskStateMachine()
{
}
````

И там и там не произойдёт аллокаций, только создать и вернуть структурку - дороже:
````
| Method                         | Mean        | Error     | StdDev    | Median      | Gen0   | Allocated |
|------------------------------- |------------:|----------:|----------:|------------:|-------:|----------:|
| TaskStateMachine               |   5.3381 ns | 0.0549 ns | 0.0514 ns |   5.3285 ns |      - |         - |
| ValueTaskStateMachine          |  10.7577 ns | 0.1564 ns | 0.1386 ns |  10.7075 ns |      - |         - |
````

А в методах с частым синхронным выполнением, и наличием возвращаемого значения - выигрыш имеется.

Есть ещё один use-case для `ValueTask`, о нём - в `Chapter_5_8_IValueTaskSource`.