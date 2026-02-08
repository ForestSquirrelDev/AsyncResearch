#### Вопрос 1: сколько весит стейт машина вот такого метода?
````csharp
public async Task MeasureBareTask()
{
    await Task.Yield();
}
````

Напрямую ответить на этот вопрос не получится, т.к. стейт машина - это тип, генерируемый компилятором. Но можно посмотреть на сгенерированные поля стейт машины:
````csharp
public int <>1__state;
public AsyncTaskMethodBuilder <>t__builder;
private YieldAwaitable.YieldAwaiter <>u__1;
````

- `int` + 4 байта на padding: 8 байт; 
- Ссылка на `builder`: 8 байт; 
- Пустая структура `YieldAwaiter`: 0 байт, плюс выравнивание - 8 байт

Итого, вместе с выравниванием, такая стейт машина займёт в куче 24 байта.

#### Вопрос 2: сколько весит `Task` вместе со стейт машиной вот такого метода?
````csharp
public async Task MeasureBareTask()
{
    await Task.Yield();
}
````

Ответ: 96 байт.
````
| MeasureBareTask          |        618.0 ns |      1.89 ns |      1.77 ns | 0.0114 |      96 B |
````

#### Вопрос 3: сколько весит `Task` вместе со стейт машиной и инфраструктурой таймера?
````csharp
[Benchmark]
public async Task MeasureTaskDelay()
{
    await Task.Delay(1);
}
````

Ответ: 328 байт.
````
| MeasureTaskDelay         | 15,504,376.5 ns | 66,613.34 ns | 62,310.16 ns |      - |     328 B |
````

#### Вопрос 4: захват переменных в стейт машину напрямую влияет на её размер в куче?
````csharp
[Benchmark]
public async Task MeasureTaskDelayWithLong()
{
    long myData = 42;
    await Task.Delay(1);
    _ = myData;
}
````

Ответ: да. Захваченный long увеличил стейт машину с таймером ровно на 8 байт:
````csharp
| MeasureTaskDelayWithLong | 15,494,544.1 ns | 82,025.59 ns | 76,726.78 ns |      - |     336 B |
````

#### Вопрос 5: десять вызванных await создадут десять тасков и стейт машин в куче?
````csharp
[Benchmark]
public async Task MeasureTenTasks()
{
    for (int i = 0; i < 10; i++)
    {
        await SimpleTask();
    }
}
````

Ответ: да. 10 подзадач по 96 байт, и сама стейт машина `MeasureTenTasks` заняли 1064 байта - у стейт машины `MeasureTenTasks` полей больше из-за цикла for.
````
| MeasureTenTasks          |      4,619.2 ns |     22.51 ns |     21.06 ns | 0.1221 |    1064 B |
````

Все тесты проводились на .NET 8.0 в Release сборке:
````
BenchmarkDotNet v0.15.8, Windows 11 (10.0.22631.5039/23H2/2023Update/SunValley3)
AMD Ryzen 7 7435H 3.10GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 8.0.410
  [Host]     : .NET 8.0.16 (8.0.16, 8.0.1625.21506), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 8.0.16 (8.0.16, 8.0.1625.21506), X64 RyuJIT x86-64-v3


| Method                   | Mean            | Error        | StdDev       | Gen0   | Allocated |
|------------------------- |----------------:|-------------:|-------------:|-------:|----------:|
| MeasureBareTask          |        618.0 ns |      1.89 ns |      1.77 ns | 0.0114 |      96 B |
| MeasureTaskDelay         | 15,504,376.5 ns | 66,613.34 ns | 62,310.16 ns |      - |     328 B |
| MeasureTaskDelayWithLong | 15,494,544.1 ns | 82,025.59 ns | 76,726.78 ns |      - |     336 B |
| MeasureTenTasks          |      4,619.2 ns |     22.51 ns |     21.06 ns | 0.1221 |    1064 B |
````