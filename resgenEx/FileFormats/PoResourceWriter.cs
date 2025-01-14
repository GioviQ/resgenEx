﻿// This file is GPL
//
// It is modified from files obtained from the mono project under GPL licence.
namespace resgenEx.FileFormats
{
    using System;
    using System.IO;
    using System.Resources;
    using System.Security.Principal;
    using System.Text;
    using System.Text.RegularExpressions;

    class PoResourceWriter : IResourceWriter
    {
        TextWriter s;
        Options options;
        bool headerWritten;
        string sourceFile = null;
        Regex regexCultureFromFileName = new Regex(@"\.(\w+)\.resx");

        /// <summary>
        /// Override this in subclass if you want to write a .pot file instead of a .po file
        /// </summary>
        protected virtual bool WriteValuesAsBlank()
        {
            return false;
        }

        public PoResourceWriter(Stream stream, Options aOptions) : this(stream, aOptions, null) { }

        public PoResourceWriter(Stream stream, Options aOptions, string aSourceFile)
        {
            // Unicode BOM causes syntax errors in the gettext utils
            Encoding utf8WithoutBom = new UTF8Encoding(false);
            s = new StreamWriter(stream, utf8WithoutBom);

            options = aOptions;
            sourceFile = aSourceFile;

            // Even if the .po file we are writing contains no items, it still needs to contain
            // the header, otherwise msgmerge will screw up when passed a 0 length file (I think
            // it screws up because without the header it makes bad judgements about the character
            // encoding it's supposed to be merging using).
            if (!headerWritten)
            {
                headerWritten = true;
                WriteHeader();
            }
        }

        public string SourceFile
        {
            get { return sourceFile; }
            set { sourceFile = value; }
        }

        StringBuilder ebuilder = new StringBuilder();

        public string Escape(string ns)
        {
            ebuilder.Length = 0;

            // the empty string is used on the first line, to allow better alignment of the multi-line string to follow
            if (ns.Contains("\n"))
                ebuilder.Append("\"\r\n\"");

            foreach (char c in ns)
            {
                switch (c)
                {
                    case '"':
                    case '\\':
                        ebuilder.Append('\\');
                        ebuilder.Append(c);
                        break;
                    case '\a':
                        ebuilder.Append("\\a");
                        break;
                    case '\n':
                        ebuilder.Append("\"\r\n\"");
                        break;
                    case '\r':
                        //ebuilder.Append("\\r");
                        break;
                    default:
                        ebuilder.Append(c);
                        break;
                }
            }
            return ebuilder.ToString();
        }

        /// <param name="commentType">
        /// If the rawComments contains a new-line, this paramter will determine
        /// which type of rawComments it will be continued as after the newline. 
        /// '\0' for a translator-rawComments, '.' for an extracted rawComments, ':' for a reference etc
        /// </param>
        /// <param name="indent">
        /// If the rawComments contains a new-line, this paramter will determine how many
        /// spaces of indent will precede the rawComments when it continues after the newline. 
        /// </param>
        public string EscapeComment(string ns, char commentType, int indent)
        {
            string newlineReplacement = "\n#";
            if (commentType != '\0') newlineReplacement += commentType;
            if (indent > 0) newlineReplacement = newlineReplacement.PadRight(newlineReplacement.Length + indent, ' ');

            return ns.Replace("\n", newlineReplacement);
        }

        /// <param name="commentType">
        /// If the rawComments contains a new-line, this paramter will determine
        /// which type of rawComments it will be continued as after the newline. 
        /// '\0' for a translator-rawComments, '.' for an extracted rawComments, ':' for a reference etc
        /// </param>
        public string EscapeComment(string ns, char commentType)
        {
            return EscapeComment(ns, commentType, 0);
        }

        public void AddResource(string name, byte[] value)
        {
            AddResource(ResourceItem.Get(name, value));
        }

        public void AddResource(string name, object value)
        {
            AddResource(ResourceItem.Get(name, value));
        }

        public void AddResource(string name, string value)
        {
            AddResource(ResourceItem.Get(name, value));
        }

        public virtual void AddResource(ResourceItem item)
        {
            if (!headerWritten)
            {
                headerWritten = true;
                WriteHeader();
            }


            if (options.Comments != CommentOptions.writeNoComments)
            {

                if (item is PoItem)
                {
                    // We can preserve the comments exactly as they were
                    s.Write(((PoItem)item).Metadata_PoRawComments);

                }
                else
                {
                    // if FullComments is set, then store the original message in a rawComments
                    // so the file could be converted into a .pot file (.po template file)
                    // without losing information.
                    string originalMessage = item.Metadata_OriginalValue;
                    string sourceReference = item.Metadata_OriginalSource;

                    if (options.Comments == CommentOptions.writeFullComments)
                    {
                        if (String.IsNullOrEmpty(originalMessage)) originalMessage = item.Value;
                        if (String.IsNullOrEmpty(sourceReference)) sourceReference = SourceFile;

                        if (item.Metadata_OriginalSourceLine > 0)
                        {
                            if (!String.IsNullOrEmpty(sourceReference)) sourceReference += ", ";
                            sourceReference += "line " + item.Metadata_OriginalSourceLine;
                        }
                    }
                    else
                    {
                        // Don't include automatically generated comments such as file reference
                        sourceReference = null;
                    }

                    if (!String.IsNullOrEmpty(item.Metadata_Comment))
                    {
                        // "#." in a .po file indicates an extracted rawComments
                        s.WriteLine("#. {0}", EscapeComment(item.Metadata_Comment, '.'));
                        if (!String.IsNullOrEmpty(originalMessage)) s.WriteLine("#. "); // leave an empty line between this rawComments and when we list the originalMessage
                    }

                    if (!String.IsNullOrEmpty(originalMessage))
                    {
                        // "#." in a .po file indicates an extracted rawComments
                        if (originalMessage.Contains("\n"))
                        {
                            // Start multi-line messages indented on a new line, and have each new line in the message indented
                            s.WriteLine(ResGen.cOriginalMessageComment_Prefix + "\n#.    " + EscapeComment(originalMessage, '.', 4));
                        }
                        else
                        {
                            s.WriteLine(ResGen.cOriginalMessageComment_Prefix + EscapeComment(originalMessage, '.', 4));
                        }
                    }

                    if (!String.IsNullOrEmpty(sourceReference))
                    {
                        // "#:" in a .po file indicates a code reference rawComments, such as the line of source code the 
                        // string is used in, currently PoResourceWriter just inserts the source file name though.
                        s.WriteLine("#: {0}", EscapeComment(sourceReference, '.'));
                    }

                    if (options.FormatFlags && (item.Metadata_Flags & TranslationFlags.csharpFormatString) != 0)
                    {
                        s.WriteLine("#, csharp-format");
                    }
                }
            }

            string value = WriteValuesAsBlank() ? String.Empty : Escape(item.Value);

            if (!string.IsNullOrWhiteSpace(options.MsgCtxt))
                s.WriteLine("msgctxt \"{0}\"", Escape(options.MsgCtxt));

            s.WriteLine("msgid \"{0}\"", Escape(item.Name));
            s.WriteLine("msgstr \"{0}\"", value);
            s.WriteLine("");
        }

        void WriteHeader()
        {
            s.WriteLine("# This file was generated by " + ResGen.cProgramNameShort + " " + ResGen.ProgramVersion);
            if (!String.IsNullOrEmpty(SourceFile))
            {
                s.WriteLine("#");
                s.WriteLine("# Converted to PO from:");
                s.WriteLine("#   " + sourceFile);
            }
            s.WriteLine("#");
            s.WriteLine("#, fuzzy"); // this flag will cause this header item to be ignored as a msgid when converted to .resx
            s.WriteLine("msgid \"\"");
            s.WriteLine("msgstr \"\"");
            s.WriteLine("\"MIME-Version: 1.0\\n\"");
            s.WriteLine("\"Content-Type: text/plain; charset=UTF-8\\n\"");
            s.WriteLine("\"Content-Transfer-Encoding: 8bit\\n\"");
            s.WriteLine("\"X-Generator: AdvaTel resgenEx 0.11\\n\"");
            s.WriteLine("\"Project-Id-Version: PACKAGE VERSION\\n\"");
            s.WriteLine("\"PO-Revision-Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:MMzzzz") + "\\n\"");

            string usersIdentity = WindowsIdentity.GetCurrent().Name;
            if (String.IsNullOrEmpty(usersIdentity)) usersIdentity = "NAME";
            int slashPos = usersIdentity.LastIndexOf('\\');
            if (slashPos >= 0 && slashPos < usersIdentity.Length) usersIdentity = usersIdentity.Substring(slashPos + 1); // Drop the domain name from the user name, if it's present
            s.WriteLine("\"Last-Translator: " + Escape(usersIdentity) + " <EMAIL@ADDRESS>\\n\"");

            var fileName = Path.GetFileName(sourceFile);

            var culture = "en-US";

            if (regexCultureFromFileName.IsMatch(fileName))
                culture = regexCultureFromFileName.Match(fileName).Groups[1].Value;

            s.WriteLine($"\"Language: {culture}\\n\"");
            s.WriteLine("\"Language-Team: English\\n\"");
            s.WriteLine("\"Report-Msgid-Bugs-To: \\n\"");
            s.WriteLine("\"Plural-Forms: nplurals=2; plural=(n != 1);\\n\"");


            s.WriteLine();
        }

        public void Close()
        {
            s.Close();
        }

        public void Dispose() { }

        public void Generate() { }
    }
}
