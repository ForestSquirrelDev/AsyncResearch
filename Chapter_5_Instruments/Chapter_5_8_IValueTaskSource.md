Возьмём следующий пример. Локальный сервер на `Socket` поднимается на порт 8080, ждёт подключения клиента, и принимает от него десять тысяч пакетов по байту:
````csharp
public static async Task RunMemoryTestServer()
{
    using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    listener.Bind(new IPEndPoint(IPAddress.Any, 8080));
    listener.Listen(1);
    Console.WriteLine("Сервер: Жду подключения на порту 8080...");

    using var client = await listener.AcceptAsync();
    // Отключаем алгоритм Нагла, чтобы пакеты не склеивались (для чистоты эксперимента)
    client.NoDelay = true; 
    Console.WriteLine("Сервер: Клиент подключился!");

    // Читаем по одному байту
    var buffer = new byte[1];
    var iterations = 10000;

    // Прогрев
    for (var i = 0; i < 100; i++) await client.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);

    long totalBytes = 0;
    for (var i = 0; i < iterations; i++)
    {
        GC.TryStartNoGCRegion(1024);
        
        var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        var read = await client.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);
        var bytesAfter = GC.GetAllocatedBytesForCurrentThread();
        totalBytes += Math.Max(bytesAfter - bytesBefore, 0);
        
        GC.EndNoGCRegion();
        
        if (read == 0) break;

        if (i % 2000 == 0) 
            Console.WriteLine($"Сервер: Обработано {i} байт...");
    }

    Console.WriteLine("\n--- ИТОГ ЗАМЕРА ---");
    Console.WriteLine($"Выделено памяти: {totalBytes} байт");
    Console.WriteLine($"На одну операцию: {(double)(totalBytes) / iterations} байт");
}
````
Из Python скриптика, клиент шлёт этому локальному серверу по одному байту десять тысяч раз:
````cs
import socket
import time


def run_client():
    server_address = ('127.0.0.1', 8080)
    iterations = 10000

    print(f"Клиент: Подключение к {server_address}...")
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            sock.connect(server_address)

            print(f"Клиент: Начинаю отправку {iterations} байт...")

            data = b'x'
            for i in range(iterations):
                sock.sendall(data)

            print("Клиент: Отправка завершена.")
    except Exception as e:
        print(f"Ошибка: {e}")


if __name__ == "__main__":
    run_client()
````

C# сервер считает кол-во аллоцированных байтов на каждую операцию чтения, и сохраняет результат. Вывод программы будет следующим:
````
Сервер: Жду подключения на порту 8080...
Сервер: Клиент подключился!
Сервер: Обработано 0 байт...
Сервер: Обработано 2000 байт...
Сервер: Обработано 4000 байт...
Сервер: Обработано 6000 байт...
Сервер: Обработано 8000 байт...

--- ИТОГ ЗАМЕРА ---
Выделено памяти: 0 байт
На одну операцию: 0 байт

Process finished with exit code 0.
````

Мы синхронно отправили 10000 пакетов, и синхронно их прочитали. Аллокаций не произошло. 

Когда мы вызываем `ReceiveAsync()`, происходит следующее. Мы берём кэшированный `_singleBufferReceiveEventArgs`:
````csharp
  internal ValueTask<int> ReceiveAsync( // Socket.cs, CS: 4943
    Memory<byte> buffer,
    SocketFlags socketFlags,
    bool fromNetworkStream,
    CancellationToken cancellationToken)
  {
    if (cancellationToken.IsCancellationRequested)
      return ValueTask.FromCanceled<int>(cancellationToken);
    Socket.AwaitableSocketAsyncEventArgs socketAsyncEventArgs = Interlocked.Exchange<Socket.AwaitableSocketAsyncEventArgs>(ref this._singleBufferReceiveEventArgs, (Socket.AwaitableSocketAsyncEventArgs) null) ?? new Socket.AwaitableSocketAsyncEventArgs(this, true);
    socketAsyncEventArgs.SetBuffer(buffer);
    socketAsyncEventArgs.SocketFlags = socketFlags;
    socketAsyncEventArgs.WrapExceptionsForNetworkStream = fromNetworkStream;
    return socketAsyncEventArgs.ReceiveAsync(this, cancellationToken);
  }
````
Чуть ниже по иерархии - пытаемся подождать Async, но у нас не получается - `SocketError` оказывается `Success`, т.е. мы прочитали байтики синхронно:
````csharp
  public ValueTask<int> ReceiveAsync(Socket socket, CancellationToken cancellationToken) // Socket.cs, CS: 5913
  {
      if (socket.ReceiveAsync((SocketAsyncEventArgs) this, cancellationToken))
      {   this._cancellationToken = cancellationToken;   return new ValueTask<int>((IValueTaskSource<int>) this, this._mrvtsc.Version);
      }
      int bytesTransferred = this.BytesTransferred;
      SocketError socketError = this.SocketError;
      this.ReleaseForSyncCompletion();
      return socketError != SocketError.Success ? ValueTask.FromException<int>(this.CreateException(socketError)) : new ValueTask<int>(bytesTransferred);
  }
  ...
  private bool ReceiveAsync(SocketAsyncEventArgs e, CancellationToken cancellationToken) // Socket.cs, CS: 3745
  {
    this.ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull((object) e, nameof (e));
    e.StartOperationCommon(this, SocketAsyncOperation.Receive);
    SocketError socketError;
    try
    {
      socketError = e.DoOperationReceive(this._handle, cancellationToken);
    }
    catch
    {
      e.Complete();
      throw;
    }
    // socketError --> Success
    return socketError == SocketError.IOPending;
  }
````
В результате вызывается метод `ReleaseForSyncCompletion`, который возвращает `AwaitableSocketAsyncEventArgs` в кэш, если мы не успели начать выполнять ещё одну операцию:
````csharp
private void ReleaseForSyncCompletion() // Socket.cs, CS: 5889
{
  if (Interlocked.CompareExchange<Socket.AwaitableSocketAsyncEventArgs>(ref this._isReadForCaching ? ref this._owner._singleBufferReceiveEventArgs : ref this._owner._singleBufferSendEventArgs, this, (Socket.AwaitableSocketAsyncEventArgs) null) == null)
    return;
  this.Dispose();
}
````
А `ReceiveAsync` возвращает нам `ValueTask` с байтиками. Аллокаций в кучу не произошло, т.к. нам вернули структурку.

Если добавить ожидание в скрипт на питончике, то аллокации появятся - стейт машину придётся упаковывать в кучу:
````csharp
Сервер: Жду подключения на порту 8080...
Сервер: Клиент подключился!
Сервер: Обработано 0 байт...
Сервер: Обработано 2000 байт...
Сервер: Обработано 4000 байт...
Сервер: Обработано 6000 байт...
Сервер: Обработано 8000 байт...

--- ИТОГ ЗАМЕРА ---
Выделено памяти: 4804192 байт
На одну операцию: 480 байт

Process finished with exit code 0.
````

А `ManualResetValueTaskSource`, который используется как объект для реализации потокобезопасной логики `IValueTaskSource`, будет ресетиться:
````csharp
public void Reset() // ManualResetValueTaskSource.cs, CS: 51
{
    _version++;
    _continuation = null;
    _continuationState = null;
    _capturedContext = null;
    _error = null;
    _result = default;
    _completed = false;
}
````
Этот ресет - причина, по которой мы получим исключение, если попытаемся сделать `await` в нашем примере дважды. Версия объекта будет другой. 
А в худшем случае - мы получим не те данные, ведь если версия почему-то совпала, то там уже будет совсем другой входящий запрос.

Старая перегрузка с `Task<int>` проигрывает даже в таком сценарии, с разницей в ~10 раз:
````csharp
Сервер: Жду подключения на порту 8080...
Сервер: Клиент подключился!
Сервер: Обработано 0 байт...
Сервер: Обработано 2000 байт...
Сервер: Обработано 4000 байт...
Сервер: Обработано 6000 байт...
Сервер: Обработано 8000 байт...

--- ИТОГ ЗАМЕРА ---
Выделено памяти: 49312016 байт
На одну операцию: 4931 байт

Process finished with exit code 0.
````

Хотя эта перегрузка в итоге тоже использует путь с `ValueTask`, ей приходится вызывать `AsTask()`, что приводит к созданию цепочки объектов в куче:
````csharp
  internal Task<int> ReceiveAsync( // Socket.cs, CS: 4906
    ArraySegment<byte> buffer,
    SocketFlags socketFlags,
    bool fromNetworkStream)
  {
    Socket.ValidateBuffer(buffer);
    return this.ReceiveAsync((Memory<byte>) buffer, socketFlags, fromNetworkStream, new CancellationToken()).AsTask();
  }
````

Таким образом, `Socket` использует `IValueTaskSource` как своего рода awaitable "пул" из одного объекта, который она переиспользует, и сбрасывает при наличии асинхронного ожидания.
Примерно то же самое можно было бы сделать и на неких кастомных пулах или кэшированных объектах, но `IValueTaskSource` - это именно то, что позволяет написать `await` за счёт перегрузки
`ValueTask`, принимающей `ValueTaskSource`:
````csharp
public ValueTask(IValueTaskSource source, short token) // ValueTask.cs, CS: 91
{
    if (source == null)
    {
        ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
    }

    _obj = source;
    _token = token;

    _continueOnCapturedContext = true;
}
````
`ValueTask` становится своего рода прокси-объектом, перенаправляя от него свойства и методы, например:
````csharp
public bool IsCompleted // ValueTask.cs, CS: 295
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get
    {
        object? obj = _obj;
        Debug.Assert(obj == null || obj is Task || obj is IValueTaskSource);

        if (obj == null)
        {
            return true;
        }

        if (obj is Task t)
        {
            return t.IsCompleted;
        }

        return Unsafe.As<IValueTaskSource>(obj).GetStatus(_token) != ValueTaskSourceStatus.Pending;
    }
}
````
В итоге получается некое соглашение между нашей кастомной реализацией кэшированного объекта, и контрактом асинхронности в рантайме .NET. Мы говорим рантайму: вот наш кэш, вот свойства
и методы которые ты от нас требуешь реализовать по контракту. Рантайм говорит: ок, теперь ваш кастомный кэш можно авейтить.

И как бонус, `ValueTask` также позволяет не возвращать `Task` при синхронном ожидании - т.е. возвращать значение на стеке без аллокаций в пул.