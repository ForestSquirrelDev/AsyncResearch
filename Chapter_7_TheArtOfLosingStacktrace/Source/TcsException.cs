namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class TcsException
    {
        public static async Task TcsExceptionTest()
        {
            var tcs = new TaskCompletionSource();
            try
            {
                throw new Exception("HORY SHIET!");
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            
            Victim(tcs);
            await Task.Delay(3000);
        }

        private static async void Victim(TaskCompletionSource tcs)
        {
            await tcs.Task;
        }
    }
}