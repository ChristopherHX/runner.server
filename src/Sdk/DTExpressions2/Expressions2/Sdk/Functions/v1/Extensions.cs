namespace GitHub.DistributedTask.Expressions2.Sdk.Functions.v1 {
    public static class Extensions {
        private static Legacy.EvaluationResult ToLegacy(EvaluationContext context, EvaluationResult node) {
            return new Legacy.EvaluationResult(context, 0, node.Value, node.Kind, node.Raw, true);
        }
        public static bool EvaluateBoolean(this ExpressionNode node, EvaluationContext context) {
            return ToLegacy(context, node.Evaluate(context)).ConvertToBoolean(context);
        }

        public static string EvaluateString(this ExpressionNode node, EvaluationContext context) {
            return ToLegacy(context, node.Evaluate(context)).ConvertToString(context);
        }

        public static bool Equals(this EvaluationResult node, EvaluationContext context, EvaluationResult other) {
            return ToLegacy(context, node).Equals(context, ToLegacy(context, other));
        }

        public static bool TryConvertToString(this EvaluationResult node, EvaluationContext context, out string str) {
            return ToLegacy(context, node).TryConvertToString(context, out str);
        }

        public static int CompareTo(this EvaluationResult node, EvaluationContext context, EvaluationResult other) {
            return ToLegacy(context, node).CompareTo(context, ToLegacy(context, other));
        }
    }
}