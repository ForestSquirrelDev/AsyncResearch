// в каком этапе кадра вызывается exec синхронизационного контекста?

`UnitySynchronizationContext` - это хороший пример того, когда может понадобиться SynchronizationContext.
Большая часть кода движка Unity не потокобезопасна, поэтому разработчики движка запрещают его выполнение из других потоков. 

Если MoveNext() асинхронной стейт машины вызовут из другого потока, то весь код, следующий за `await`, тоже продолжит выполняться из этого потока.
Но тогда возможности async в юнити были бы крайне ограничены.

Чтобы решить эту проблему, юнитеки создали `UnitySynchronizationContext`. 

Возьмём для примера следующий Unity Script:
````
public class SampleScript : UnityEngine.MonoBehaviour
{
    async void Start()
    {
        await System.Threading.Tasks.Task.Delay(1000);
        UnityEngine.Debug.Log("Hello World!");
    }
}
````

Здесь мы сделали метод движка `Start` асинхронным, и самое главное - вызвали `Task.Delay`: Delay создаст DelayPromise, и `Task.FinishContinuations()` будет вызван из ThreadPool, т.е. с большой вероятностью это будет не основной поток.
Если бы у юнити не было `UnitySynchronizationContext`, вызов `MoveNext()` у асинхронной стейт машины метода `Start()` привёл бы к тому, что `UnityEngine.Debug.Log("Hello World!")` вызовется из соседнего потока.

Но этого не происходит, т.к. `SynchronizationContextAwaitTaskContinuation` кладёт `MoveNext()` в очередь внутри `UnitySynchronizationContext`:
````
public override void Post(SendOrPostCallback callback, object state) // UnitySynchronizationContext.cs, CS: 59
{
  lock (this.m_AsyncWorkQueue)
    this.m_AsyncWorkQueue.Add(new UnitySynchronizationContext.WorkRequest(callback, state));
}
````

Весь стактрейс выглядит следующим образом:
````
UnitySynchronizationContext.Post()
SynchronizationContextAwaitTaskContinuation.PostAction()
AwaitTaskContinuation.RunCallback()
SynchronizationContextAwaitTaskContinuation.Run()
Task.FinishContinuations()
Task.FinishStageThree()
Task<VoidTaskResult>.TrySetResult()
Task.DelayPromise.Complete()
Task.<>c.<Delay>b__247_1()
Timer.Scheduler.TimerCB()
QueueUserWorkItemCallback.System.Threading.IThreadPoolWorkItem.ExecuteWorkItem()
ThreadPoolWorkQueue.Dispatch()
_ThreadPoolWaitCallback.PerformWaitCallback()
````

В разные моменты в течение `PlayerLoop`, C++ часть движка вызывает метод `ExecuteTasks`:
````
[RequiredByNativeCode]
private static void ExecuteTasks() // UnitySynchronizationContext.cs, CS: 94
{
  if (!(SynchronizationContext.Current is UnitySynchronizationContext current))
    return;
  current.Exec();
}
````

И мы начнём в главном потоке юнити выполнять коллбэки тасок, которые ранее упали в очередь:
````
public void Exec() // UnitySynchronizationContext.cs, CS: 70
{
  lock (this.m_AsyncWorkQueue)
  {
    this.m_CurrentFrameWork.AddRange((IEnumerable<UnitySynchronizationContext.WorkRequest>) this.m_AsyncWorkQueue);
    this.m_AsyncWorkQueue.Clear();
  }
  while (this.m_CurrentFrameWork.Count > 0)
  {
    UnitySynchronizationContext.WorkRequest workRequest = this.m_CurrentFrameWork[0];
    this.m_CurrentFrameWork.RemoveAt(0);
    workRequest.Invoke();
  }
}
````

`Exec` вызывается не один раз за PlayerLoop. Как [пишут](https://discussions.unity.com/t/why-await-resumes-on-the-main-thread-in-unity-synchronizationcontext/1700147) сами юнитеки, он вызывается несколько раз в течение PlayerLoop:
`Unity then processes this queue at specific points during the Unity PlayerLoop. In other words, they are executed as part of Unity’s normal frame update cycle.`

Однако, как правило async continuations всё равно выполняются в следующем кадре:
`Because async continuations are queued and later flushed from the PlayerLoop, they are typically executed on the next frame. This is the root cause of the commonly observed “one-frame delay” in Unity async code.`

В результате асинхронная стейт машина завершится в основном потоке, и весь код после 'await' тоже будет выполнен в главном потоке. Что позволяет Unity не ограничивать вызов нативных функций движка в асинхронных методах.

