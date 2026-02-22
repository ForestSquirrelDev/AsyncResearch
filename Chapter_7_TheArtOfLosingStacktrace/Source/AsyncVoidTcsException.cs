namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class AsyncVoidTcsException
    {
        public static async Task AsyncVoidExceptionTest()
        {
            TaskCompletionSource tcs = new TaskCompletionSource();
            _ = Layer0(tcs);
            await tcs.Task;
        }

        private static async Task Layer0(TaskCompletionSource tcs)
        {
            await Task.Delay(100);
            await Layer1(tcs);
        }

        private static async Task Layer1(TaskCompletionSource tcs)
        {
            await Task.Delay(100);
            await Layer2(tcs);
        }
        
        private static async Task Layer2(TaskCompletionSource tcs)
        {
            await Task.Delay(100);
            await Layer3(tcs);
        }

        private static async Task Layer3(TaskCompletionSource tcs)
        {
            await Task.Delay(100);
            Layer4(tcs);
        }
        
        private static async void Layer4(TaskCompletionSource tcs)
        {
            await Task.Delay(100);
            tcs.SetException(new Exception("HORY SHIET!"));
        }
    }
}