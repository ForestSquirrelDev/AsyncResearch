namespace AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class AwaitUsingAggregateExceptionExample
    {
        public static async Task RunAwaitUsingExample()
        {
            var resource = new AsyncResource();
            try
            {
                resource.DoWork();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Поймали исключение: {ex.Message}");
                try
                {
                    await resource.DisposeAsync();
                }
                catch (Exception disposeEx)
                {
                    throw new AggregateException(ex, disposeEx);
                }
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