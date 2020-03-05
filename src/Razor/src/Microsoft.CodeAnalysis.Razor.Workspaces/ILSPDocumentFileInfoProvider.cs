using System;

namespace Microsoft.CodeAnalysis.Razor.Workspaces
{
    internal interface ILSPDocumentFileInfoProvider
    {
        public void UpdateFileInfo(Uri uri, ICSharpOutputContainer csharpOutputContainer);
    }

    internal interface ICSharpOutputContainer
    {
        public TextLoader CreateGeneratedTextLoader(string filePath);
    }
}
