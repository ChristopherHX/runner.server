using System;

namespace GitHub.DistributedTask.Expressions2.Sdk.Functions.v1
{
    internal sealed class SubString : Function
    {
        protected sealed override Boolean TraceFullyRealized => false;

        protected sealed override Object EvaluateCore(EvaluationContext context, out ResultMemory memory)
        {
            memory = null;
            String left = Parameters[0].EvaluateString(context) as String ?? String.Empty;
            Int32 startIndex = (Int32)Parameters[1].Evaluate(context).ConvertToNumber();
            Int32 length = (Int32)Parameters[2].Evaluate(context).ConvertToNumber();
            return left.Substring(startIndex, length);
        }
    }
}
