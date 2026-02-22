namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class AwaitUsingExample
    {
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
    }
}