Пример, аналогичный разделу 6_0:
````csharp
public static class AsyncSurvivorExample
{
    public static async Task RunAsyncSurvivorExample()
    {
        var tcs = new TaskCompletionSource();
        WeakReference weakRef = null;

        void CreateAndReleaseVictim()
        {
            var victim = new AsyncSurvivor("Призрак");
            _ = victim.StayAliveAsync(tcs.Task);
            weakRef = new WeakReference(victim);
        }
        CreateAndReleaseVictim();

        Console.WriteLine("\n--- Первая попытка GC (задача еще не завершена) ---");
        CollectGarbage();

        if (weakRef?.IsAlive ?? false)
            Console.WriteLine("Результат: Объект ЖИВ. Стейт-машина держит его зубами.");
        else
            Console.WriteLine("Результат: Объект умер. (Этого не должно случиться)");

        Console.WriteLine("\n--- Завершаем задачу (SetResult) ---");
        tcs.SetResult();
    
        await Task.Yield();

        Console.WriteLine("\n--- Вторая попытка GC (задача завершена) ---");
        CollectGarbage();

        if (weakRef?.IsAlive ?? false)
            Console.WriteLine("Результат: Объект всё еще жив? (Странно)");
        else
            Console.WriteLine("Результат: Объект УНИЧТОЖЕН. Цепочка разорвана.");
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

public class AsyncSurvivor
{
    private readonly string _name;

    public AsyncSurvivor(string name) => _name = name;

    ~AsyncSurvivor() => Console.WriteLine($"[GC] {_name} был уничтожен!");

    public async Task StayAliveAsync(Task task)
    {
        Console.WriteLine($"[{_name}] Начинаю работу и жду сигнала...");
        
        await task;

        Console.WriteLine($"[{_name}] Я дождался! Мое имя всё еще: {_name}");
    }
}
````

Здесь цепочка выглядит так так:

1. Создаётся объект `AsyncSurvivor`.
2. У него вызывается асинхронный метод, который под капотом создаст стейт машину.
3. Стейт машина ожидает `Task`, который мы же сами и держим через `TaskCompletionSource`. Она добавится в `m_continuations` таска, что создаст ссылку на стейт машину.
4. На этом этапе цепочка выглядит так: `RunAsyncSurvivorExample() --> tcs --> Task --> m_continuations --> AsyncSurvivor.<StayAliveAsync>d__3 stateMachine --> AsyncSurvivor`.
5. Как только мы вызываем` tcs.SetResult()`, у `Task` вызывается `FinishContinuations()`, список `m_continuations` в полях `Task` очищается, и ссылка на стейт машину в списке удаляется.
Вместо этого там оказывается объект-пустышка `s_taskCompletionSentinel` - больше никто не сможет добавиться в `m_continuations`:
````csharp
internal void FinishContinuations() // Task.cs, CS: 3445
{
    object? continuationObject = Interlocked.Exchange(ref m_continuationObject, s_taskCompletionSentinel);
    if (continuationObject != null)
    {
        RunContinuations(continuationObject);
    }
}
````
6. Стек выполнения стейт машины `StayAliveAsync` завершается. В стейт машине остаётся ссылка на `AsyncSurvivor`, но снаружи на них никто больше не ссылается:
````csharp
  [CompilerGenerated]
  [StructLayout(LayoutKind.Auto)]
  private struct <StayAliveAsync>d__3 : 
  /*[Nullable(0)]*/
  IAsyncStateMachine
  {
    ...
    [Nullable(0)]
    public AsyncSurvivor <>4__this;
    ...

    void IAsyncStateMachine.MoveNext()
    {
      // Забрали ссылку на стейт машину
      AsyncSurvivor asyncSurvivor = this.<>4__this;
      try
      {
          ...
      }
      catch (Exception ex)
      {
          ...
      }
      this.<>1__state = -2;
      this.<>t__builder.SetResult();
      // Но в конце не очистили локальную ссылку
    }
  }
````

Вывод программы будет следующим:
````
[Призрак] Начинаю работу и жду сигнала...

--- Первая попытка GC (задача еще не завершена) ---
Результат: Объект ЖИВ. Стейт-машина держит его зубами.

--- Завершаем задачу (SetResult) ---
[Призрак] Я дождался! Мое имя всё еще: Призрак

--- Вторая попытка GC (задача завершена) ---
[GC] Призрак был уничтожен!
Результат: Объект УНИЧТОЖЕН. Цепочка разорвана.

Process finished with exit code 0.
````

Завершение `tcs.SetResult()` успешно отпустило `AsyncSurvivor` и его стейт машину.