using System.Net;
using System.Net.Sockets;

namespace AsyncResearch.Chapter_5_Instruments.Source
{
    public static class SocketServerExample
    {
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

            var buffer = new byte[1]; // Читаем по одному байту
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
    }
}