﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

/*============================================================
**
**
** 
**
**
** Purpose: Culture-specific collection of resources.
**
** 
===========================================================*/

using System;
using System.Collections;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Versioning;
using System.Diagnostics.Contracts;
using System.Collections.Generic;

namespace System.Resources
{
    // A ResourceSet stores all the resources defined in one particular CultureInfo.
    // 
    // The method used to load resources is straightforward - this class
    // enumerates over an IResourceReader, loading every name and value, and 
    // stores them in a hash table.  Custom IResourceReaders can be used.
    //
    [Serializable]
    public class ResourceSet : IDisposable, IEnumerable
    {
        [NonSerialized]
        protected IResourceReader Reader;
        private Dictionary<object, object> Table;

        private Dictionary<object, object> _caseInsensitiveTable;  // For case-insensitive lookups.

        protected ResourceSet()
        {
            // To not inconvenience people subclassing us, we should allocate a new
            // hashtable here just so that Table is set to something.
            CommonInit();
        }

        // For RuntimeResourceSet, ignore the Table parameter - it's a wasted 
        // allocation.
        internal ResourceSet(bool junk)
        {
        }

        // Creates a ResourceSet using the system default ResourceReader
        // implementation.  Use this constructor to open & read from a file 
        // on disk.
        // 
        public ResourceSet(String fileName)
        {
            Reader = new ResourceReader(fileName);
            CommonInit();
            ReadResources();
        }

        // Creates a ResourceSet using the system default ResourceReader
        // implementation.  Use this constructor to read from an open stream 
        // of data.
        // 
        public ResourceSet(Stream stream)
        {
            Reader = new ResourceReader(stream);
            CommonInit();
            ReadResources();
        }

        public ResourceSet(IResourceReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException("reader");
            Contract.EndContractBlock();
            Reader = reader;
            CommonInit();
            ReadResources();
        }

        private void CommonInit()
        {
            Table = new Dictionary<object, object>();
        }

        // Closes and releases any resources used by this ResourceSet, if any.
        // All calls to methods on the ResourceSet after a call to close may 
        // fail.  Close is guaranteed to be safely callable multiple times on a 
        // particular ResourceSet, and all subclasses must support these semantics.
        public virtual void Close()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Close the Reader in a thread-safe way.
                IResourceReader copyOfReader = Reader;
                Reader = null;
                if (copyOfReader != null)
                    copyOfReader.Close();
            }
            Reader = null;
            _caseInsensitiveTable = null;
            Table = null;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        // Returns the preferred IResourceReader class for this kind of ResourceSet.
        // Subclasses of ResourceSet using their own Readers &; should override
        // GetDefaultReader and GetDefaultWriter.
        public virtual Type GetDefaultReader()
        {
            return typeof(ResourceReader);
        }

        private Type _resourceWriterType = null;
        // Returns the preferred IResourceWriter class for this kind of ResourceSet.
        // Subclasses of ResourceSet using their own Readers &; should override
        // GetDefaultReader and GetDefaultWriter.
        public virtual Type GetDefaultWriter()
        {
            // ResourceWriter lives higher up the stack, so get it via reflection
            if (_resourceWriterType == null)
            {
                Assembly resourceWriterAssembly = Assembly.Load("System.Resources.Writer, Version=4.0.1.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
                _resourceWriterType = resourceWriterAssembly.GetType("System.Resources.ResourceWriter", true);
            }
            return _resourceWriterType;
        }

        public virtual IDictionaryEnumerator GetEnumerator()
        {
            return GetEnumeratorHelper();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumeratorHelper();
        }

        private IDictionaryEnumerator GetEnumeratorHelper()
        {
            Dictionary<object, object> copyOfTable = Table;  // Avoid a race with Dispose
            if (copyOfTable == null)
                throw new ObjectDisposedException(null, SR.ObjectDisposed_ResourceSet);
            return copyOfTable.GetEnumerator();
        }

        // Look up a string value for a resource given its name.
        // 
        public virtual String GetString(String name)
        {
            Object obj = GetObjectInternal(name);
            try
            {
                return (String)obj;
            }
            catch (InvalidCastException)
            {
                throw new InvalidOperationException(SR.Format(SR.InvalidOperation_ResourceNotString_Name, name));
            }
        }

        public virtual String GetString(String name, bool ignoreCase)
        {
            Object obj;
            String s;

            // Case-sensitive lookup
            obj = GetObjectInternal(name);
            try
            {
                s = (String)obj;
            }
            catch (InvalidCastException)
            {
                throw new InvalidOperationException(SR.Format(SR.InvalidOperation_ResourceNotString_Name, name));
            }

            // case-sensitive lookup succeeded
            if (s != null || !ignoreCase)
            {
                return s;
            }

            // Try doing a case-insensitive lookup
            obj = GetCaseInsensitiveObjectInternal(name);
            try
            {
                return (String)obj;
            }
            catch (InvalidCastException)
            {
                throw new InvalidOperationException(SR.Format(SR.InvalidOperation_ResourceNotString_Name, name));
            }
        }

        // Look up an object value for a resource given its name.
        // 
        public virtual Object GetObject(String name)
        {
            return GetObjectInternal(name);
        }

        public virtual Object GetObject(String name, bool ignoreCase)
        {
            Object obj = GetObjectInternal(name);

            if (obj != null || !ignoreCase)
                return obj;

            return GetCaseInsensitiveObjectInternal(name);
        }

        protected virtual void ReadResources()
        {
            IDictionaryEnumerator en = Reader.GetEnumerator();
            while (en.MoveNext())
            {
                Object value = en.Value;
                Table.Add(en.Key, value);
            }
            // While technically possible to close the Reader here, don't close it
            // to help with some WinRes lifetime issues.
        }

        private Object GetObjectInternal(String name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            Contract.EndContractBlock();

            Dictionary<object, object> copyOfTable = Table;  // Avoid a race with Dispose

            if (copyOfTable == null)
                throw new ObjectDisposedException(null, SR.ObjectDisposed_ResourceSet);

            return copyOfTable[name];
        }

        private Object GetCaseInsensitiveObjectInternal(String name)
        {
            Dictionary<object, object> copyOfTable = Table;  // Avoid a race with Dispose

            if (copyOfTable == null)
                throw new ObjectDisposedException(null, SR.ObjectDisposed_ResourceSet);

            Dictionary<object, object> caseTable = _caseInsensitiveTable;  // Avoid a race condition with Close
            if (caseTable == null)
            {
                caseTable = new Dictionary<object, object>(CaseInsenisitiveStringObjectComparer.Instance);

                IDictionaryEnumerator en = copyOfTable.GetEnumerator();
                while (en.MoveNext())
                {
                    caseTable.Add(en.Key, en.Value);
                }
                _caseInsensitiveTable = caseTable;
            }

            return caseTable[name];
        }

        /// <summary>
        /// Adapter for StringComparer.OrdinalIgnoreCase to allow it to be used with Dictionary
        /// </summary>
        private class CaseInsenisitiveStringObjectComparer : IEqualityComparer<object>
        {
            public static CaseInsenisitiveStringObjectComparer Instance { get; } = new CaseInsenisitiveStringObjectComparer();

            private CaseInsenisitiveStringObjectComparer() { }

            public new bool Equals(object x, object y)
            {
                return ((IEqualityComparer)StringComparer.OrdinalIgnoreCase).Equals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return ((IEqualityComparer)StringComparer.OrdinalIgnoreCase).GetHashCode(obj);
            }
        }
    }
}