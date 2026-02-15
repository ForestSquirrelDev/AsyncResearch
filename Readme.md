# Данный проект - это серия небольших технических отчётов, призванная ответить на множество вопросов, накопившихся при работе с асинхронностью в C#.

## Список глав:

### 🏗️ Chapter 1 - Архитектура: "Event-based"

О том, что такое асинхронность в C# - почему это хитрая событийная система.

### 🛡️ Chapter 2 - Исключения и EDI

- О том, как стактрейс сохраняется при прыжках между потоками: `ExceptionDispatchInfo` (EDI). Рантайм делает "снимок" стека (`DeepCopy` в массив байтов) и восстанавливает его при каждом `Throw()`.
- `UnobservedTaskException`: Исключения в "забытых" задачах всплывают только при сборке мусора (через `Finalizer`), не роняя процесс.

### 🔄 Chapter 3 - SynchronizationContext: Порядок в хаосе

Разбор работы `UnitySynchronizationContext` и кастомной реализации контекста синхронизации.

### ⚠️ Chapter 4 - Async Void: "Ручная граната"

Разбираются отличия `async void` от `async Task`.

### 🛠️ Chapter 5 - Instruments

О том, какие инструменты C# даёт нам для более тонкой настройки асинхронности.

### 🛑 Chapter 6 - Memory Leaks

О том, в каких случаях стейт-машина может навсегда остаться в управляемой куче.

### 🕵️‍♂️ Chapter 7 - The Art Of Losing Stacktrace

Об изощрённых и не очень способах потерять оригинальный стактрейс исключения, или всё исключение целиком.

### 💰 Chapter 8 - Прейскурант асинхронности (Memory Benchmarks)

Результаты замеров на x64 .NET 8 показывают цену асинхронных операций.

## Список вопросов, поставленных в основу данного исследования, и краткие ответы на них

### ❔Вопрос 1: в какой момент выполняется "продолжение" таски? Это триггер или некий Polling механизм?

📖 Ответ: Асинхронность в C# - это событийная модель. 

"Продолжением" `async` метода можно считать `MoveNext()` его стейт машины, которую сгенерировал компилятор. 
В какой момент и кем вызовется это продолжение - зависит от того, что в методе написано. 

Например, если `Task.Delay()`, - `MoveNext()` вызовет системный таймер.
Если мы ждём `TaskCompletionSource.Task`, то `MoveNext()` можем вызвать мы сами - через `TaskCompletionSource.TrySetResult()`.

### ❔Вопрос 2: На что влияет из какого потока выполнится продолжение, и как сделать так чтобы продолжение всегда выполнялось из основного потока?

📖 Ответ: то, из какого потока вызовется продолжение, зависит от того, где вызовется `MoveNext()`. Если асинхронный метод создаёт операцию в другом потоке, и тот вызовет `MoveNext()`,
выполнение стейт машины продолжится из соседнего потока. Гарантировать продолжение выполнения из основного потока можно двумя способами:
- `SynchronizationContext`.
- Ручное завершение `Task` в управляющем потоке через `TaskCompletionSource`.

### ❔Вопрос 3: В каких случаях стактрейс исключения в асинхронном методе может потеряться?

📖A: Существует три способа выбросить исключения в .NET: `throw` (только в блоке `try-catch`), `throw ex`, и `ExceptionDispatchInfo.Throw(ex)`.

В асинхронных методах .NET пробрасывает оригинальный стактрейс исключения по всей цепочке вызовов, через `ExceptionDispatchInfo.Throw(ex)`.
Самый простой способ потерять часть или весь стактрейс - это в каком-то месте сделать `throw ex`. CLR посчитает что исключение выбросили именно оттуда, 
и оригинальный стактрейс потеряется. 

Ещё один лёгкий способ - выбросить исключение в `async void` методе, либо в `async Task` методе без ожидания. Цепочка асинхронных вызовов способна пробросить исключение наверх
только за счёт того, что сгенерированная компилятором стейт машина делает `try-catch`, а затем `builder.SetException(ex)`. Если какая-то из стейт машин по пути к вызывающему
не сможет поймать исключение в `try-catch`, потому что мы не сделали `await` - часть вызывающего пути потеряется.

Например: `Method0() --> await Method1() --> await Method2() --> _ = Method3() --> throw new Exception()`. В стактрейсе исключения будет только `Method3()`, и путь откуда
у таска вызвался `FinishContinuations()`. `Method2()` не знает что в `Method3()` произошло исключение, просто потому что он не вызвал `Method3().GetAwaiter().GetResult()`.

Дальше идут более изощрённые способы потерять исключение:
- Можно вызвать `AggregateException.Stacktrace` и потерять весь список исключений;
- Можно сохранить объект исключения в `TaskCompletionSource` или `Task.FromException(ex)`, но сделать `await` вообще в другом месте;
- Можно поймать исключение в `Task.WhenAny()` - по умолчанию он кушает вообще исключения;
- Можно поймать несколько исключений в `Task.WhenAll()` - выброшено будет только первое;
- Можно вызвать `TaskCancelledException` или `TimeoutException` раньше чем таска отработает и выбросит своё исключение;
- Можно вызвать `ContinueWith(...)` и не обработать `Faulted` таску.

### ❔Вопрос 4: Когда мы говорим об аллокациях на Стейт машину, о каких размерах вообще идёт речь?

📖Ответ: Сама по себе сырая сгенерированная компилятором стейт машина, без захвата контекста, весит немного: 24 байта. 

Но чем больше у неё обвязочной инфраструктуры и захваченных переменных, тем она дороже: метод с `await Task.Yield()` будет аллоцировать 96 байт - там стейт машина превращается
в `AsyncStateMachineBox`, который наследуется от `Task<TResult>`.

### ❔Вопрос 5: Как исключение в асинхронном методе пробрасывается наверх по стеку вызовов?

📖Ответ: возьмём пример `async Method0() --> await Method1() --> { await Task.Delay(100); throw new Exception("Die!"); }`. 
Для обоих асинхронных методов сгенерируется стейт машина. Внутри стейт машины `Method1()`, в упрощённом виде, будет сгенерировано примерно следующее:
````csharp
// Первая итерация MoveNext()
...
try 
{
    awaiter = Task.Delay(100).GetAwaiter();
    if (!awaiter.IsCompleted)
    {
        // this это стейт машина
        awaiter.AwaitUnsafeOnCompleted(this);
        return;
    }
    ...
    // Вторая итерация MoveNext(), вызванная срабатыванием таймера созданного внутри Task.Delay():
    awaiter.GetResult();
    throw new Exception("Die!");
}
catch (Exception ex)
{
    this.state = -2;
    this.builder.SetException(ex);
    return;
}
...
````
То есть стейт машина метода `Method1()`, при втором вызове `MoveNext()`, т.е. в коде после `await` - кинула исключение и сама же его поймала. Когда она его ловит, она вызывает
`builder.SetException()`. Если `builder` это `AsyncTaskMethodBuilder`, то исключение сохранится в поля объекта класса `Task`. `SetException()` также пометит таск как `Faulted`. 
Когда `Task` переходит в состояние `Faulted`, он вызывает `FinishContinuations()`, внутри которого будет лежать `MoveNext()` стейт машины `Method0()`.

Это пнёт стейт машину `Method0()`, где сгенерировалась аналогичная логика: она тоже сделала `Method1().GetAwaiter()`. И она тоже вызовет `GetResult()`.

А `GetResult()` внутри себя сделает проверку: у меня есть исключения? Тогда `exceptionDispatchInfos[0].Throw()`. Так исключение попадает в `Method0()`: стейт машина кладёт его в `Task`,
и `Task` путешествует по стеку вызовов асинхронных методов.

### ❔Вопрос 6: Что на самом деле происходит под капотом, когда мы говорим "асинхронный код выполнится синхронно"?

📖Ответ: Возьмём пример. `async Method0() --> await MyMethod1() --> Console.WriteLine("Hello, World!")`. Стейт машина `Method1()`, в упрощённом виде, сгенерируется примерно в следующее:
````csharp
// Первая и последняя итерация MoveNext()
try 
{
    Console.WriteLine("Hello, World!");
}
catch (Exception ex)
{
    ...
}
this.state = -2;
this.builder.SetResult();
````
А стейт машина `Method0()` сгенерируется примерно в такое:
````csharp
// Первая итерация MoveNext()
try 
{
    awaiter = Method1().GetAwaiter();
    // Вызов Method1() уже спровоцировал Console.Writeline() на том же потоке, где запущен Method0(). Затем у Task, который Method1() возвращает, создаётся awaiter через GetAwaiter().
    // И синхронность здесь будет заключаться в том, что Awaiter будет сразу завершённым. Сгенерированная стейт машина Method1() завершится после первой же итерации MoveNext(), 
    // потому что в ней нет никакого синхронного ожидания.
    if (!awaiter.IsCompleted) 
    {
        // Мы сюда не попадём
        awaiter.AwaitUnsafeOnCompleted(this);
        return;
    }
}
catch (Exception ex)
{
    ...
}
this.state = -2;
this.builder.SetResult();
````

Таким образом, структурка `TaskAwaiter` метода `Method1()` оказалась завершённой сразу после создания, и стейт машине `Method0()` не пришлось упаковываться в `AsyncStateMachineBox()`,
чтобы её `MoveNext()` пнули по завершении асинхронной операции. Метод `Method1().GetAwaiter()` вернул управление сразу, и стейт машина `Method0()` продолжила выполняться на том же потоке,
без аллокаций в кучу.

### ❔Вопрос 7: Стейт машина создастся, даже если код исполнится синхронно?

📖Ответ: Да. Компилятор создаст стейт машину на любой `async` метод.

### ❔Вопрос 8: Если стейт машина исполнится синхронно, она всё равно аллоцируется в куче, или нет?

📖Ответ: Нет. Стейт машина сразу вернёт управление вызывающему методу/стейт машине, без аллокаций в управляемой куче.

### ❔Вопрос 9: Чем отличается `async Void` от `async Task`?

📖Ответ: Как и для `async Task` методов, для `async void` компилятор будет генерировать стейт машину. У неё будет свой экземпляр `AsyncVoidMethodBuilder`, а у того внутри - 
`AsyncTaskMethodBuilder --> Task`.

Это нужно, чтобы `async void` стейт машина могла переиспользовать всю ту же логику `Task Parallel Library`: например, когда `async void` стейт машина встретит ожидание, она в итоге вызовет
тот же метод `AwaitUnsafeOnCompleted`, что и `async Task`. При исключении - `builder.SetException(ex)`, и исключение в итоге тоже засетится во внутренний `Task` у `AsyncTaskMethodBuilder`. 
И так далее.

Но отличия тоже есть:
1. Внутри метода `AsyncVoidMethodBuilder.Create()`, `AsyncVoidMethodBuilder` уведомляет текущий контекст синхронизации (если такой предоставлен) о начале асинхронной операции:
````csharp
public static AsyncVoidMethodBuilder Create() // AsyncVoidMethodBuilder.cs, CS: 23
{
    SynchronizationContext? sc = SynchronizationContext.Current; 
    sc?.OperationStarted();

    return new AsyncVoidMethodBuilder() { _synchronizationContext = sc };
}
````
2. Когда `async void` стейт машина вызовет у своего `builder` метод `SetResult()`, тот проставит `SetResult()` у своего внутреннего `AsyncTaskMethodBuilder`. 
Причём он сделает это независимо от того, завершилась таска успехом, или нет:
````csharp
...
_builder.SetResult(); // AsyncVoidMethodBuilder.cs, CS: 99
...
````
3. `SetException()`. В отличие от `AsyncTaskMethodBuilder`, `AsyncVoidMethodBuilder` не сохраняет исключение внутрь `Task`, чтобы вызывающий сам решил, как и когда ему обрабатывать
(или не обрабатывать) лежащее там исключение. `AsyncVoidMethodBuilder` сразу пытается выбросить исключение: и делает он это, в зависимости от наличия `SynchronizationContext`- либо прямо в контекст:
````csharp
SynchronizationContext? context = _synchronizationContext; // AsyncVoidMethodBuilder.cs, CS: 123
if (context != null)
{
    try
    {
        Task.ThrowAsync(exception, targetContext: context);
    }
    finally
    {
        NotifySynchronizationContextOfCompletion(context);
    }
}
````
Либо в `ThreadPool`:
````csharp
...
ThreadPool.QueueUserWorkItem(static state => ((ExceptionDispatchInfo)state!).Throw(), edi); // Task.cs, CS: 1929
...
````
4. И самое очевидное отличие - `async void` метод нельзя подождать, ведь для ожидания нужно получить `TaskAwaiter` (или кастомную структуру ожидания, если реализуется кастомный `AsyncBuilder`),
а `void` авейтер не вернёт.

Получается, что `async void` - это хитрое переиспользование `Task`, только с сигнатурой `void`, и более агрессивным выбросом исключений.

### ❔Вопрос 10: Цепочка вызовов из 10 `async` методов лишь с одним ожиданием в конце цепочки, создаёт 10 тасок и кладёт в кучу 10 `AsyncStateMachineBox`?

📖Ответ: Да. Если цепочка вызовов доходит до момента, где операция не завершена синхронно (т.е. вызывается `builder.AwaitUnsafeOnCompleted(...)`), 
рантайму приходится сохранять состояние стека в куче.

Рантайм создаст 10 объектов `AsyncStateMachineBox<TStateMachine>`:
````csharp
private class AsyncStateMachineBox<TStateMachine> : // AsyncTaskMethodBuilderT.cs, CS: 275
    Task<TResult>, IAsyncStateMachineBox
    where TStateMachine : IAsyncStateMachine
...
````
Каждый такой объект является одновременно и контейнером для стейт-машины (ее полей и переменных), и самим объектом `Task`, так как он наследуется от `Task<TResult>`.

### ❔Вопрос 11: Почему говорят, что "исключение в async void может крашнуть приложение (хочется спросить: а что, может и не крашнуть?😊)"?

📖Ответ: `AsyncVoidMethodBuilder`, в условиях отсутствия контекста синхронизации, выбросит исключение прямо в `ThreadPool`:
````csharp
...
ThreadPool.QueueUserWorkItem(static state => ((ExceptionDispatchInfo)state!).Throw(), edi); // Task.cs, CS: 1929
...
````
Как пишут сами разработчики .NET, `This will result in a crash unless legacy exception behavior is enabled by a config file or a CLR host.`
Рантайм убьёт весь процесс. Причём ремарка про _legacy exception behavior_ здесь не просто так: раньше .NET работал ровно наоборот - ну упало исключение в `ThreadPool` и упало,
"бог бы с ним". 

Но такое поведение приводило к непредсказуемым последствиям: в [статье](https://learn.microsoft.com/en-us/archive/msdn-magazine/2005/july/unhandled-exceptions-and-tracing-in-the-net-framework-2-0)
разработчик .NET Джон Роббинс рассказывает, как было "до" и "после". До .NET Framework 2.0, потоки могли тихо умирать один за другим, и это невозможно было заметить:
процесс деградировал, а сообщений об ошибках не было.

Поэтому переход к политике "fail fast", когда CLR убивает всё приложение с `UnhandledException` - это _"mandatory upgrade"_.

### ❔Вопрос 11.1: почему в Unity исключение в `async void` не крашнет приложение, как предвещают разработчики .NET?

📖Ответ: Unity предоставляет собственный контекст синхронизации `UnitySynchronizationContext`. А при наличии контекста синхронизации, `AsyncVoidMethodBuilder` положит исключение туда, 
а не в `ThreadPool`:
````csharp
SynchronizationContext? context = _synchronizationContext; // AsyncVoidMethodBuilder.cs, CS: 123
if (context != null)
{
    try
    {
        Task.ThrowAsync(exception, targetContext: context);
    }
    finally
    {
        NotifySynchronizationContextOfCompletion(context);
    }
}
````
В итоге исключение попадёт в метод `Post()` класса `UnitySynchronizationContext`:
````csharp
public override void Post(SendOrPostCallback callback, object state) // UnitySynchronizationContext.cs, CS: 59
{
  lock (this.m_AsyncWorkQueue)
    this.m_AsyncWorkQueue.Add(new UnitySynchronizationContext.WorkRequest(callback, state));
}
````
И выбросится тогда, когда `PlayerLoop` в C++ части движка в следующий раз начнёт разбирать очередь запланированных задач:
````csharp
[RequiredByNativeCode]
private static void ExecuteTasks() // UnitySynchronizationContext.cs, CS: 94
{
  if (!(SynchronizationContext.Current is UnitySynchronizationContext current))
    return;
  current.Exec();
}
````
Таким образом, исключение в `async void` в контексте движка `Unity` положит не весь процесс, а лишь конкретную итерацию `PlayerLoop`.

### ❔Вопрос 12: Что будет если стейт машина А, вызовет создание стейт машины Б, начнёт ждать `Continuation`, и станет Eligible For GC (на неё ни у кого не осталось ссылок)?

📖 Ответ: Зависит от того, что происходит внутри стейт машин А и Б. Если там происходит что-то, по механизму похожее на `Task.Delay()` - объект будет жить до тех пор,
пока стейт машина не завершилась. Как GC Root в данном случае выступит статическая очередь таймеров, которая используется в рантайме при `Task.Delay()`:
`[GC Root (очередь таймеров)] --> [Task стейт-машины Б] --> [m_continuations] --> [AsyncStateMachineBox стейт-машины А]`. Т.е. если стейт машина Б зацепилась за внешний корень,
она будет жива, и по цепочке удержит стейт машину А.

Если только стейт машина А зацепилась за корень, то стейт машину Б тоже нельзя будет забрать. Тут получатся замыкание: стейт машина А держит стейт машину Б через ссылку на `Task`,
а стейт машина Б держит стейт машину А через `m_continuations`.

Но если на стейт машину А и Б больше никто не ссылается извне, то вся цепочка (А и Б) будет собрана GC, даже если задачи не завершены:
`[async Method0()] --> _ = [StateMachineA()] --> [await StateMachineB()]`.

Самый нижний `Task` в цепочке - в каком-то смысле самый важный:
- Если в самом низу стоит `Task.Delay()`, - вся цепочка выживет.
- Если в самом низу стоит `HttpClient.GetAsync()`, - вся цепочка выживет, т.к. сетевой стек рантайма будет держать задачу, пока не придет ответ от ОС.
- Если вызовов, приводящих к связи с GC Root в `StateMachineB` нет, то и всю цепочку можно будет собрать.

### ❔Вопрос 13: Что, если MoveNext стейт машины никогда не вызовут? Она навсегда останется лежать в управляемой куче?

📖 Ответ: Как и в вопросе 12, зависит от того, ссылается ли кто-то на стейт машину. Если мы сделали Fire and forget: `[MyMethod()] --> [_ = MyAsyncMethod()]`, и стейт машина
не сделала вызовов, приводящих к её связи с GC Root, - например добавление в очередь таймеров через `Task.Delay()`, то GC просто соберёт стейт машину.

Если же ссылка с GC Root образовалась, то да, стейт машина останется лежать в управляемой куче до тех пор, пока ссылка не порвётся.
