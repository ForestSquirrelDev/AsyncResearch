namespace AsyncResearch.Chapter_5_Instruments.Source
{
    public static class TaskDelayCancellationTokenExample
    {
        public static async Task RunTaskDelayCancellationTokenExample()
        {
            var tokenSource = new CancellationTokenSource();
            var t = CancellableTaskDelay(tokenSource.Token);
            tokenSource.Cancel();
            await t;
        }

        private static async Task CancellableTaskDelay(CancellationToken token)
        {
            await Task.Delay(1000, token);
        }
    }
}