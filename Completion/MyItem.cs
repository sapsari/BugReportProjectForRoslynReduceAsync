using Microsoft.CodeAnalysis.Completion;
using System;
using System.Collections.Generic;
using System.Text;

namespace Completion
{
    internal readonly struct MyItem
    {
        public readonly string DisplayText;
        public readonly string InlineDescription;
        public readonly string FullDescription;
        public readonly CompletionChange Change;
        public readonly bool IsDefault;

        public MyItem(
            string displayText, string inlineDescription, string fullDescription, CompletionChange change, bool isDefault)
        {
            DisplayText = displayText;
            InlineDescription = inlineDescription;
            FullDescription = fullDescription;
            Change = change;
            IsDefault = isDefault;
        }
    }
}
