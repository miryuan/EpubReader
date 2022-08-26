﻿using System;
using System.IO;
using System.Threading.Tasks;
using VersOne.Epub.Environment;
using VersOne.Epub.Internal;
using VersOne.Epub.Options;

namespace VersOne.Epub
{
    /// <summary>
    /// The base class for a content file reference within the EPUB archive (e.g., HTML files or images).
    /// Unlike <see cref="EpubContentFile" />, the classes derived from this base class contain only a reference to the file but don't contain its content.
    /// </summary>
    public abstract class EpubContentFileRef
    {
        private readonly EpubBookRef epubBookRef;
        private readonly ContentReaderOptions contentReaderOptions;
        private ReplacementContentFileEntry replacementContentFileEntry;

        /// <summary>
        /// Initializes a new instance of the <see cref="EpubContentFileRef" /> class with a specified EPUB book reference, a file name, a content type of the file,
        /// and a MIME type of the file's content.
        /// </summary>
        /// <param name="epubBookRef">EPUB book reference object which contains this content file reference.</param>
        /// <param name="fileName">Relative file path of the content file (as it is specified in the EPUB manifest).</param>
        /// <param name="contentType">The type of the content of the file.</param>
        /// <param name="contentMimeType">The MIME type of the content of the file.</param>
        /// <param name="contentReaderOptions">Optional content reader options determining how to handle missing content files.</param>
        protected EpubContentFileRef(EpubBookRef epubBookRef, string fileName, EpubContentType contentType, string contentMimeType, ContentReaderOptions contentReaderOptions = null)
        {
            this.epubBookRef = epubBookRef;
            FileName = fileName;
            FilePathInEpubArchive = ZipPathUtils.Combine(epubBookRef.Schema.ContentDirectoryPath, FileName);
            ContentType = contentType;
            ContentMimeType = contentMimeType;
            this.contentReaderOptions = contentReaderOptions;
            replacementContentFileEntry = null;
        }

        /// <summary>
        /// Gets the relative file path of the content file (as it is specified in the EPUB manifest).
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Gets the absolute file path of the content file in the EPUB archive.
        /// </summary>
        public string FilePathInEpubArchive { get; }

        /// <summary>
        /// Gets the type of the content of the file.
        /// </summary>
        public EpubContentType ContentType { get; }

        /// <summary>
        /// Gets the MIME type of the content of the file.
        /// </summary>
        public string ContentMimeType { get; }

        /// <summary>
        /// Reads the whole content of the referenced file and returns it as a byte array.
        /// </summary>
        /// <returns>Content of the referenced file.</returns>
        public byte[] ReadContentAsBytes()
        {
            return ReadContentAsBytesAsync().Result;
        }

        /// <summary>
        /// Asynchronously reads the whole content of the referenced file and returns it as a byte array.
        /// </summary>
        /// <returns>A task that represents the asynchronous read operation. The value of the TResult parameter contains the content of the referenced file.</returns>
        public async Task<byte[]> ReadContentAsBytesAsync()
        {
            IZipFileEntry contentFileEntry = GetContentFileEntry();
            byte[] content = new byte[(int)contentFileEntry.Length];
            using (Stream contentStream = contentFileEntry.Open())
            using (MemoryStream memoryStream = new MemoryStream(content))
            {
                await contentStream.CopyToAsync(memoryStream).ConfigureAwait(false);
            }
            return content;
        }

        /// <summary>
        /// Reads the whole content of the referenced file and returns it as a string.
        /// </summary>
        /// <returns>Content of the referenced file.</returns>
        public string ReadContentAsText()
        {
            return ReadContentAsTextAsync().Result;
        }

        /// <summary>
        /// Asynchronously reads the whole content of the referenced file and returns it as a string.
        /// </summary>
        /// <returns>A task that represents the asynchronous read operation. The value of the TResult parameter contains the content of the referenced file.</returns>
        public async Task<string> ReadContentAsTextAsync()
        {
            using (Stream contentStream = GetContentStream())
            using (StreamReader streamReader = new StreamReader(contentStream))
            {
                return await streamReader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Opens the referenced file and returns a <see cref="Stream" /> to access its content.
        /// </summary>
        /// <returns>A <see cref="Stream" /> to access the referenced file's content.</returns>
        public Stream GetContentStream()
        {
            return GetContentFileEntry().Open();
        }

        private IZipFileEntry GetContentFileEntry()
        {
            if (replacementContentFileEntry != null)
            {
                return replacementContentFileEntry;
            }
            if (String.IsNullOrEmpty(FileName))
            {
                throw new EpubPackageException("EPUB parsing error: file name of the specified content file is empty.");
            }
            string contentFilePath = FilePathInEpubArchive;
            IZipFileEntry contentFileEntry = epubBookRef.EpubFile.GetEntry(contentFilePath);
            if (contentFileEntry == null)
            {
                bool throwMissingFileException = true;
                if (contentReaderOptions != null)
                {
                    ContentFileMissingEventArgs contentFileMissingEventArgs = new ContentFileMissingEventArgs(FileName, FilePathInEpubArchive, ContentType, ContentMimeType);
                    contentReaderOptions.RaiseContentFileMissingEvent(contentFileMissingEventArgs);
                    if (contentFileMissingEventArgs.ReplacementContentStream != null)
                    {
                        replacementContentFileEntry = new ReplacementContentFileEntry(contentFileMissingEventArgs.ReplacementContentStream);
                        contentFileEntry = replacementContentFileEntry;
                        throwMissingFileException = false;
                    }
                    else if (contentFileMissingEventArgs.SuppressException)
                    {
                        replacementContentFileEntry = new ReplacementContentFileEntry(new MemoryStream());
                        contentFileEntry = replacementContentFileEntry;
                        throwMissingFileException = false;
                    }
                }
                if (throwMissingFileException)
                {
                    throw new EpubContentException($"EPUB parsing error: file \"{contentFilePath}\" was not found in the EPUB file.", contentFilePath);
                }
            }
            if (contentFileEntry.Length > Int32.MaxValue)
            {
                throw new EpubContentException($"EPUB parsing error: file \"{contentFilePath}\" is larger than 2 GB.", contentFilePath);
            }
            return contentFileEntry;
        }

        private sealed class ReplacementContentFileEntry : IZipFileEntry
        {
            private readonly byte[] replacementStreamContent;

            public ReplacementContentFileEntry(Stream replacementStream)
            {
                using (replacementStream)
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    replacementStream.CopyTo(memoryStream);
                    replacementStreamContent = memoryStream.ToArray();
                }
            }

            public long Length => replacementStreamContent.Length;

            public Stream Open()
            {
                return new MemoryStream(replacementStreamContent);
            }
        }
    }
}
