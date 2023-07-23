using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;


namespace Completion
{
    [ExportCompletionProvider(nameof(MyCompletionProvider), LanguageNames.CSharp)]
    internal class MyCompletionProvider : CompletionProvider
    {
        private const string StartKey = nameof(StartKey);
        private const string LengthKey = nameof(LengthKey);
        private const string NewTextKey = nameof(NewTextKey);
        private const string DescriptionKey = nameof(DescriptionKey);

        // Always soft-select these completion items.  Also, never filter down.
        private static readonly CompletionItemRules s_rules =
            CompletionItemRules.Default.WithSelectionBehavior(CompletionItemSelectionBehavior.SoftSelection)
                                       .WithFilterCharacterRules(ImmutableArray.Create(CharacterSetModificationRule.Create(CharacterSetModificationKind.Replace)));

        public ImmutableHashSet<char> TriggerCharacters { get; } = ImmutableHashSet.Create('<');


        public override bool ShouldTriggerCompletion(SourceText text, int caretPosition, CompletionTrigger trigger, OptionSet options)
        {
            if (trigger.Kind == CompletionTriggerKind.Invoke ||
                trigger.Kind == CompletionTriggerKind.InvokeAndCommitIfUnique)
            {
                return true;
            }

            if (trigger.Kind == CompletionTriggerKind.Insertion)
            {
                if (TriggerCharacters.Contains(trigger.Character))
                {
                    return true;
                }

                // Only trigger if it's the first character of a sequence
                return char.IsLetter(trigger.Character) &&
                       caretPosition >= 2 &&
                       !char.IsLetter(text[caretPosition - 2]);
            }

            return false;
        }

        public override async Task ProvideCompletionsAsync(CompletionContext context)
        {
            if (context.Trigger.Kind != CompletionTriggerKind.Invoke &&
                context.Trigger.Kind != CompletionTriggerKind.InvokeAndCommitIfUnique &&
                context.Trigger.Kind != CompletionTriggerKind.Insertion)
            {
                return;
            }

            var document = context.Document;
            var position = context.Position;
            var cancellationToken = context.CancellationToken;

            var text = await document.GetTextAsync(cancellationToken);

            var startPosition = context.Position;
            while (char.IsLetter(text[startPosition - 1]))
            {
                startPosition--;
            }

            var _replacementSpan = TextSpan.FromBounds(startPosition, context.Position);


            var itemsPlain = new string[3]
            {
                "System.IO.Directory",
                "System.IO.File",
                "System.IO.Path",
            };

            var items = new List<MyItem>();
            foreach (var ip in itemsPlain)
                items.Add(new MyItem(
                            ip, ip, ip,
                            CompletionChange.Create(
                                new TextChange(_replacementSpan, ip)),
                            isDefault: false));

            foreach (var embeddedItem in items)
            {
                var textChange = embeddedItem.Change.TextChange;

                var properties = ImmutableDictionary.CreateBuilder<string, string>();
                properties.Add(StartKey, textChange.Span.Start.ToString());
                properties.Add(LengthKey, textChange.Span.Length.ToString());
                properties.Add(NewTextKey, textChange.NewText);
                properties.Add(DescriptionKey, embeddedItem.FullDescription);
                properties.Add(/*AbstractEmbeddedLanguageCompletionProvider.EmbeddedProviderName*/"EmbeddedProvider", /*Name*/GetType().FullName);


                context.AddItem(CompletionItem.Create(
                    displayText: embeddedItem.DisplayText,
                    inlineDescription: embeddedItem.InlineDescription,
                    properties: properties.ToImmutable(),
                    rules: embeddedItem.IsDefault
                        ? s_rules.WithMatchPriority(MatchPriority.Preselect)
                        : s_rules));
            }

            context.IsExclusive = true;
        }



        public override async Task<CompletionChange> GetChangeAsync(Document document, CompletionItem item, char? commitKey, CancellationToken cancellationToken)
        {
            // These values have always been added by us.
            var startString = item.Properties[StartKey];
            var lengthString = item.Properties[LengthKey];
            var newText = item.Properties[NewTextKey];


            const bool TEST_REDUCE_ASYNC = false;


            if (TEST_REDUCE_ASYNC)
            {
                var reducedText = await GetSimplifiedTypeNameAsync(document, item.Span, newText, null, cancellationToken);
                return CompletionChange.Create(new TextChange(new TextSpan(int.Parse(startString), int.Parse(lengthString)), reducedText));
            }
            else
            {
                return CompletionChange.Create(new TextChange(new TextSpan(int.Parse(startString), int.Parse(lengthString)), newText));
            }
        }

        /// <summary>
        /// For a specified snippet field, replace it with the fully qualified name then simplify in the context of the document
        /// in order to retrieve the simplified type name.
        /// </summary>
        //public static async Task<string?> GetSimplifiedTypeNameAsync(Document document, TextSpan fieldSpan, string fullyQualifiedTypeName, SimplifierOptions simplifierOptions, CancellationToken cancellationToken)
        public static async Task<string> GetSimplifiedTypeNameAsync(Document document, TextSpan fieldSpan, string fullyQualifiedTypeName, OptionSet simplifierOptions, CancellationToken cancellationToken)
        {
            // Insert the function parameter (fully qualified type name) into the document.
            var updatedTextSpan = new TextSpan(fieldSpan.Start, fullyQualifiedTypeName.Length);

            var textChange = new TextChange(fieldSpan, fullyQualifiedTypeName);
            //var textChange = new TextChange(updatedTextSpan, fullyQualifiedTypeName);
            //var text = await document.GetValueTextAsync(cancellationToken).ConfigureAwait(false);
            var text = await document.GetTextAsync(cancellationToken);
            var documentWithFullyQualifiedTypeName = document.WithText(text.WithChanges(textChange));

            // Simplify
            var simplifiedTypeName = await GetSimplifiedTypeNameAtSpanAsync(documentWithFullyQualifiedTypeName, updatedTextSpan, simplifierOptions, cancellationToken);
            return simplifiedTypeName;
        }

        //private static async Task<string?> GetSimplifiedTypeNameAtSpanAsync(Document documentWithFullyQualifiedTypeName, TextSpan fullyQualifiedTypeSpan, SimplifierOptions simplifierOptions, CancellationToken cancellationToken)
        private static async Task<string> GetSimplifiedTypeNameAtSpanAsync(Document documentWithFullyQualifiedTypeName, TextSpan fullyQualifiedTypeSpan, OptionSet simplifierOptions, CancellationToken cancellationToken)
        {
            // Simplify
            var typeAnnotation = new SyntaxAnnotation();
            //var syntaxRoot = await documentWithFullyQualifiedTypeName.GetRequiredSyntaxRootAsync(cancellationToken);
            var syntaxRoot = await documentWithFullyQualifiedTypeName.GetSyntaxRootAsync(cancellationToken);
            var nodeToReplace = syntaxRoot.DescendantNodes().FirstOrDefault(n => n.Span == fullyQualifiedTypeSpan);

            if (nodeToReplace == null)
            {
                return null;
            }

            var updatedRoot = syntaxRoot.ReplaceNode(nodeToReplace, nodeToReplace.WithAdditionalAnnotations(typeAnnotation, Simplifier.Annotation));
            var documentWithAnnotations = documentWithFullyQualifiedTypeName.WithSyntaxRoot(updatedRoot);

            var simplifiedDocument = await Simplifier.ReduceAsync(documentWithAnnotations, simplifierOptions, cancellationToken);
            //var simplifiedRoot = await simplifiedDocument.GetRequiredSyntaxRootAsync(cancellationToken);
            var simplifiedRoot = await simplifiedDocument.GetSyntaxRootAsync(cancellationToken);
            var simplifiedTypeName = simplifiedRoot.GetAnnotatedNodesAndTokens(typeAnnotation).Single().ToString();
            return simplifiedTypeName;
        }
    }
}
