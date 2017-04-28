﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Lucene.Net.Util;
using Umbraco.Core.Configuration;
using Umbraco.ModelsBuilder.Api;
using Umbraco.ModelsBuilder.Configuration;

namespace Umbraco.ModelsBuilder.Building
{
    /// <summary>
    /// Implements a builder that works by writing text.
    /// </summary>
    internal class TextBuilder : Builder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TextBuilder"/> class with a list of models to generate
        /// and the result of code parsing.
        /// </summary>
        /// <param name="typeModels">The list of models to generate.</param>
        /// <param name="parseResult">The result of code parsing.</param>
        public TextBuilder(IList<TypeModel> typeModels, ParseResult parseResult)
            : base(typeModels, parseResult)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="TextBuilder"/> class with a list of models to generate,
        /// the result of code parsing, and a models namespace.
        /// </summary>
        /// <param name="typeModels">The list of models to generate.</param>
        /// <param name="parseResult">The result of code parsing.</param>
        /// <param name="modelsNamespace">The models namespace.</param>
        public TextBuilder(IList<TypeModel> typeModels, ParseResult parseResult, string modelsNamespace)
            : base(typeModels, parseResult, modelsNamespace)
        { }

        // internal for unit tests only
        internal TextBuilder()
        { }

        /// <summary>
        /// Outputs a generated model to a string builder.
        /// </summary>
        /// <param name="sb">The string builder.</param>
        /// <param name="typeModel">The model to generate.</param>
        public void Generate(StringBuilder sb, TypeModel typeModel)
        {
            WriteHeader(sb);

            foreach (var t in TypesUsing)
                sb.AppendFormat("using {0};\n", t);

            sb.Append("\n");
            sb.AppendFormat("namespace {0}\n", GetModelsNamespace());
            sb.Append("{\n");

            WriteContentType(sb, typeModel);

            sb.Append("}\n");
        }

        /// <summary>
        /// Outputs generated models to a string builder.
        /// </summary>
        /// <param name="sb">The string builder.</param>
        /// <param name="typeModels">The models to generate.</param>
        public void Generate(StringBuilder sb, IEnumerable<TypeModel> typeModels)
        {
            WriteHeader(sb);

            foreach (var t in TypesUsing)
                sb.AppendFormat("using {0};\n", t);

            // assembly attributes marker
            sb.Append("\n//ASSATTR\n");

            sb.Append("\n");
            sb.AppendFormat("namespace {0}\n", GetModelsNamespace());
            sb.Append("{\n");

            foreach (var typeModel in typeModels)
            {
                WriteContentType(sb, typeModel);
                sb.Append("\n");
            }

            sb.Append("}\n");
        }

        /// <summary>
        /// Outputs an "auto-generated" header to a string builder.
        /// </summary>
        /// <param name="sb">The string builder.</param>
        public static void WriteHeader(StringBuilder sb)
        {
            TextHeaderWriter.WriteHeader(sb);
        }

        private void WriteContentType(StringBuilder sb, TypeModel type)
        {
            string sep;

            if (type.IsMixin)
            {
                // write the interface declaration
                sb.AppendFormat("\t// Mixin content Type {0} with alias \"{1}\"\n", type.Id, type.Alias);
                if (!string.IsNullOrWhiteSpace(type.Name))
                    sb.AppendFormat("\t/// <summary>{0}</summary>\n", XmlCommentString(type.Name));
                sb.AppendFormat("\tpublic partial interface I{0}", type.ClrName);
                var implements = type.BaseType == null || type.BaseType.IsContentIgnored
                    ? (type.HasBase ? null : "PublishedContent")
                    : type.BaseType.ClrName;
                if (implements != null)
                    sb.AppendFormat(" : I{0}", implements);

                // write the mixins
                sep = implements == null ? ":" : ",";
                foreach (var mixinType in type.DeclaringInterfaces.OrderBy(x => x.ClrName))
                {
                    sb.AppendFormat("{0} I{1}", sep, mixinType.ClrName);
                    sep = ",";
                }

                sb.Append("\n\t{\n");

                // write the properties - only the local (non-ignored) ones, we're an interface
                var more = false;
                foreach (var prop in type.Properties.Where(x => !x.IsIgnored).OrderBy(x => x.ClrName))
                {
                    if (more) sb.Append("\n");
                    more = true;
                    WriteInterfaceProperty(sb, prop);
                }

                sb.Append("\t}\n\n");
            }

            // write the class declaration
            if (type.IsRenamed)
                sb.AppendFormat("\t// Content Type {0} with alias \"{1}\"\n", type.Id, type.Alias);
            if (!string.IsNullOrWhiteSpace(type.Name))
                sb.AppendFormat("\t/// <summary>{0}</summary>\n", XmlCommentString(type.Name));
            // cannot do it now. see note in ImplementContentTypeAttribute
            //if (!type.HasImplement)
            //    sb.AppendFormat("\t[ImplementContentType(\"{0}\")]\n", type.Alias);
            sb.AppendFormat("\t[PublishedContentModel(\"{0}\")]\n", type.Alias);
            sb.AppendFormat("\tpublic partial class {0}", type.ClrName);
            var inherits = type.HasBase
                ? null // has its own base already
                : (type.BaseType == null || type.BaseType.IsContentIgnored
                    ? GetModelsBaseClassName()
                    : type.BaseType.ClrName);
            if (inherits != null)
                sb.AppendFormat(" : {0}", inherits);

            sep = inherits == null ? ":" : ",";
            if (type.IsMixin)
            {
                // if it's a mixin it implements its own interface
                sb.AppendFormat("{0} I{1}", sep, type.ClrName);
            }
            else
            {
                // write the mixins, if any, as interfaces
                // only if not a mixin because otherwise the interface already has them already
                foreach (var mixinType in type.DeclaringInterfaces.OrderBy(x => x.ClrName))
                {
                    sb.AppendFormat("{0} I{1}", sep, mixinType.ClrName);
                    sep = ",";
                }
            }

            // begin class body
            sb.Append("\n\t{\n");

            // write the constants
            // as 'new' since parent has its own - or maybe not - disable warning
            sb.Append("#pragma warning disable 0109 // new is redundant\n");
            sb.AppendFormat("\t\tpublic new const string ModelTypeAlias = \"{0}\";\n",
                type.Alias);
            sb.AppendFormat("\t\tpublic new const PublishedItemType ModelItemType = PublishedItemType.{0};\n",
                type.ItemType);
            sb.Append("#pragma warning restore 0109\n\n");

            // write the ctor
            if (!type.HasCtor)
                sb.AppendFormat("\t\tpublic {0}(IPublishedContent content)\n\t\t\t: base(content)\n\t\t{{ }}\n\n",
                    type.ClrName);

            // write the static methods
            // as 'new' since parent has its own - or maybe not - disable warning
            sb.Append("#pragma warning disable 0109 // new is redundant\n");
            sb.Append("\t\tpublic new static PublishedContentType GetModelContentType()\n");
            sb.Append("\t\t{\n\t\t\treturn PublishedContentType.Get(ModelItemType, ModelTypeAlias);\n\t\t}\n");
            sb.Append("#pragma warning restore 0109\n\n");
            sb.AppendFormat("\t\tpublic static PublishedPropertyType GetModelPropertyType<TValue>(Expression<Func<{0}, TValue>> selector)\n",
                type.ClrName);
            sb.Append("\t\t{\n\t\t\treturn PublishedContentModelUtility.GetModelPropertyType(GetModelContentType(), selector);\n\t\t}\n");

            // write the properties
            WriteContentTypeProperties(sb, type);

            // close the class declaration
            sb.Append("\t}\n");
        }

        private void WriteContentTypeProperties(StringBuilder sb, TypeModel type)
        {
            var staticMixinGetters = UmbracoConfig.For.ModelsBuilder().StaticMixinGetters;

            // write the properties
            foreach (var prop in type.Properties.Where(x => !x.IsIgnored).OrderBy(x => x.ClrName))
                WriteProperty(sb, type, prop, staticMixinGetters && type.IsMixin ? type.ClrName : null);

            // no need to write the parent properties since we inherit from the parent
            // and the parent defines its own properties. need to write the mixins properties
            // since the mixins are only interfaces and we have to provide an implementation.

            // write the mixins properties
            foreach (var mixinType in type.ImplementingInterfaces.OrderBy(x => x.ClrName))
                foreach (var prop in mixinType.Properties.Where(x => !x.IsIgnored).OrderBy(x => x.ClrName))
                    if (staticMixinGetters)
                        WriteMixinProperty(sb, prop, mixinType.ClrName);
                    else
                        WriteProperty(sb, mixinType, prop);
        }

        private void WriteMixinProperty(StringBuilder sb, PropertyModel property, string mixinClrName)
        {
            sb.Append("\n");

            // Adds xml summary to each property containing
            // property name and property description
            if (!string.IsNullOrWhiteSpace(property.Name) || !string.IsNullOrWhiteSpace(property.Description))
            {
                sb.Append("\t\t///<summary>\n");

                if (!string.IsNullOrWhiteSpace(property.Description))
                    sb.AppendFormat("\t\t/// {0}: {1}\n", XmlCommentString(property.Name), XmlCommentString(property.Description));
                else
                    sb.AppendFormat("\t\t/// {0}\n", XmlCommentString(property.Name));

                sb.Append("\t\t///</summary>\n");
            }

            sb.AppendFormat("\t\t[ImplementPropertyType(\"{0}\")]\n", property.Alias);

            sb.Append("\t\tpublic ");
            WriteClrType(sb, property.ClrType);

            sb.AppendFormat(" {0}\n\t\t{{\n\t\t\tget {{ return ",
                property.ClrName);
            WriteNonGenericClrType(sb, GetModelsNamespace() + "." + mixinClrName);
            sb.AppendFormat(".{0}(this); }}\n\t\t}}\n",
                MixinStaticGetterName(property.ClrName));
        }

        private static string MixinStaticGetterName(string clrName)
        {
            return string.Format(UmbracoConfig.For.ModelsBuilder().StaticMixinGetterPattern, clrName);
        }

        private void WriteProperty(StringBuilder sb, TypeModel type, PropertyModel property, string mixinClrName = null)
        {
            var mixinStatic = mixinClrName != null;

            sb.Append("\n");

            if (property.Errors != null)
            {
                sb.Append("\t\t/*\n");
                sb.Append("\t\t * THIS PROPERTY CANNOT BE IMPLEMENTED, BECAUSE:\n");
                sb.Append("\t\t *\n");
                var first = true;
                foreach (var error in property.Errors)
                {
                    if (first) first = false;
                    else sb.Append("\t\t *\n");
                    foreach (var s in SplitError(error))
                    {
                        sb.Append("\t\t * ");
                        sb.Append(s);
                        sb.Append("\n");
                    }
                }
                sb.Append("\t\t *\n");
                sb.Append("\n");
            }

            // Adds xml summary to each property containing
            // property name and property description
            if (!string.IsNullOrWhiteSpace(property.Name) || !string.IsNullOrWhiteSpace(property.Description))
            {
                sb.Append("\t\t///<summary>\n");

                if (!string.IsNullOrWhiteSpace(property.Description))
                    sb.AppendFormat("\t\t/// {0}: {1}\n", XmlCommentString(property.Name), XmlCommentString(property.Description));
                else
                    sb.AppendFormat("\t\t/// {0}\n", XmlCommentString(property.Name));

                sb.Append("\t\t///</summary>\n");
            }

            sb.AppendFormat("\t\t[ImplementPropertyType(\"{0}\")]\n", property.Alias);

            if (mixinStatic)
            {
                sb.Append("\t\tpublic ");
                WriteClrType(sb, property.ClrType);
                sb.AppendFormat(" {0}\n\t\t{{\n\t\t\tget {{ return {1}(this); }}\n\t\t}}\n",
                    property.ClrName, MixinStaticGetterName(property.ClrName));
            }
            else
            {
                sb.Append("\t\tpublic ");
                WriteClrType(sb, property.ClrType);
                sb.AppendFormat(" {0}\n\t\t{{\n\t\t\tget {{ return this.GetPropertyValue",
                    property.ClrName);
                if (property.ClrType != typeof(object))
                {
                    sb.Append("<");
                    WriteClrType(sb, property.ClrType);
                    sb.Append(">");
                }
                sb.AppendFormat("(\"{0}\"); }}\n\t\t}}\n",
                    property.Alias);
            }

            if (property.Errors != null)
            {
                sb.Append("\n");
                sb.Append("\t\t *\n");
                sb.Append("\t\t */\n");
            }

            if (!mixinStatic) return;

            var mixinStaticGetterName = MixinStaticGetterName(property.ClrName);

            if (type.StaticMixinMethods.Contains(mixinStaticGetterName)) return;

            sb.Append("\n");

            if (!string.IsNullOrWhiteSpace(property.Name))
                sb.AppendFormat("\t\t/// <summary>Static getter for {0}</summary>\n", XmlCommentString(property.Name));

            sb.Append("\t\tpublic static ");
            WriteClrType(sb, property.ClrType);
            sb.AppendFormat(" {0}(I{1} that) {{ return that.GetPropertyValue",
                mixinStaticGetterName, mixinClrName);
            if (property.ClrType != typeof(object))
            {
                sb.Append("<");
                WriteClrType(sb, property.ClrType);
                sb.Append(">");
            }
            sb.AppendFormat("(\"{0}\"); }}\n",
                property.Alias);
        }

        private static IEnumerable<string> SplitError(string error)
        {
            var p = 0;
            while (p < error.Length)
            {
                var n = p + 50;
                while (n < error.Length && error[n] != ' ') n++;
                if (n >= error.Length) break;
                yield return error.Substring(p, n - p);
                p = n + 1;
            }
            if (p < error.Length)
                yield return error.Substring(p);
        }

        private void WriteInterfaceProperty(StringBuilder sb, PropertyModel property)
        {
            if (property.Errors != null)
            {
                sb.Append("\t\t/*\n");
                sb.Append("\t\t * THIS PROPERTY CANNOT BE IMPLEMENTED, BECAUSE:\n");
                sb.Append("\t\t *\n");
                var first = true;
                foreach (var error in property.Errors)
                {
                    if (first) first = false;
                    else sb.Append("\t\t *\n");
                    foreach (var s in SplitError(error))
                    {
                        sb.Append("\t\t * ");
                        sb.Append(s);
                        sb.Append("\n");
                    }
                }
                sb.Append("\t\t *\n");
                sb.Append("\n");
            }

            if (!string.IsNullOrWhiteSpace(property.Name))
                sb.AppendFormat("\t\t/// <summary>{0}</summary>\n", XmlCommentString(property.Name));
            sb.Append("\t\t");
            WriteClrType(sb, property.ClrType);
            sb.AppendFormat(" {0} {{ get; }}\n",
                property.ClrName);

            if (property.Errors != null)
            {
                sb.Append("\n");
                sb.Append("\t\t *\n");
                sb.Append("\t\t */\n");
            }
        }

        // internal for unit tests
        internal void WriteClrType(StringBuilder sb, Type type)
        {
            var s = type.ToString();

            if (type.IsGenericType)
            {
                var p = s.IndexOf('`');
                WriteNonGenericClrType(sb, s.Substring(0, p));
                sb.Append("<");
                var args = type.GetGenericArguments();
                for (var i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    WriteClrType(sb, args[i]);
                }
                sb.Append(">");
            }
            else
            {
                WriteNonGenericClrType(sb, s);
            }
        }

        private void WriteNonGenericClrType(StringBuilder sb, string s)
        {

            // takes care eg of "System.Int32" vs. "int"
            if (TypesMap.TryGetValue(s.ToLowerInvariant(), out string typeName))
            {
                sb.Append(typeName);
                return;
            }

            // if full type name matches a using clause, strip
            // so if we want Umbraco.Core.Models.IPublishedContent
            // and using Umbraco.Core.Models, then we just need IPublishedContent
            typeName = s;
            string typeUsing = null;
            var p = typeName.LastIndexOf('.');
            if (p > 0)
            {
                var x = typeName.Substring(0, p);
                if (Using.Contains(x))
                {
                    typeName = typeName.Substring(p + 1);
                    typeUsing = x;
                }
            }

            // nested types *after* using
            typeName = typeName.Replace("+", ".");

            // symbol to test is the first part of the name
            // so if type name is Foo.Bar.Nil we want to ensure that Foo is not ambiguous
            p = typeName.IndexOf('.');
            var symbol = p > 0 ? typeName.Substring(0, p) : typeName;

            // what we should find - WITHOUT any generic <T> thing - just the type
            // no 'using' = the exact symbol
            // a 'using' = using.symbol
            var match = typeUsing == null ? symbol : (typeUsing + "." + symbol);

            // if not ambiguous, be happy
            if (!IsAmbiguousSymbol(symbol, match))
            {
                sb.Append(typeName);
                return;
            }

            // symbol is ambiguous
            // if no 'using', must prepend global::
            if (typeUsing == null)
            {
                sb.Append("global::");
                sb.Append(s.Replace("+", "."));
                return;
            }

            // could fullname be non-ambiguous?
            // note: all-or-nothing, not trying to segment the using clause
            typeName = s.Replace("+", ".");
            p = typeName.IndexOf('.');
            symbol = typeName.Substring(0, p);
            match = symbol;

            // still ambiguous, must prepend global::
            if (IsAmbiguousSymbol(symbol, match))
                sb.Append("global::");

            sb.Append(typeName);
        }

        private static string XmlCommentString(string s)
        {
            return s.Replace('<', '{').Replace('>', '}').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static readonly IDictionary<string, string> TypesMap = new Dictionary<string, string>
        {
            { "system.int16", "short" },
            { "system.int32", "int" },
            { "system.int64", "long" },
            { "system.string", "string" },
            { "system.object", "object" },
            { "system.boolean", "bool" },
            { "system.void", "void" },
            { "system.char", "char" },
            { "system.byte", "byte" },
            { "system.uint16", "ushort" },
            { "system.uint32", "uint" },
            { "system.uint64", "ulong" },
            { "system.sbyte", "sbyte" },
            { "system.single", "float" },
            { "system.double", "double" },
            { "system.decimal", "decimal" }
        };
    }
}
