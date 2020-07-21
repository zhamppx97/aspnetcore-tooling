// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.CodeAnalysis.Razor.Serialization;

namespace Microsoft.CodeAnalysis.Razor.Workspaces.Serialization
{
    internal class FullProjectSnapshotHandleJsonConverter2 : JsonConverter<FullProjectSnapshotHandle>
    {
        public static readonly FullProjectSnapshotHandleJsonConverter2 Instance = new FullProjectSnapshotHandleJsonConverter2();
        private const string SerializationFormatPropertyName = "SerializationFormat";

        public override FullProjectSnapshotHandle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            string serializationFormat = null;
            string filePath = null;
            RazorConfiguration configuration = null;
            string rootNamespace = null;
            ProjectWorkspaceState projectWorkspaceState = null;
            DocumentSnapshotHandle[] documents = null;


            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        var propertyName = reader.GetString();


                        switch (propertyName)
                        {
                            case SerializationFormatPropertyName:
                                if (reader.Read())
                                {
                                    serializationFormat = (string)reader.GetString();
                                }
                                break;
                            case nameof(FullProjectSnapshotHandle.FilePath):
                                if (reader.Read())
                                {
                                    filePath = (string)reader.GetString();
                                }
                                break;
                            case nameof(FullProjectSnapshotHandle.Configuration):
                                if (reader.Read())
                                {
                                    configuration = RazorConfigurationJsonConverter2.Instance.Read(ref reader, typeof(RazorConfiguration), options) as RazorConfiguration;
                                }
                                break;
                            case nameof(FullProjectSnapshotHandle.RootNamespace):
                                if (reader.Read())
                                {
                                    rootNamespace = (string)reader.GetString();
                                }
                                break;
                            case nameof(FullProjectSnapshotHandle.ProjectWorkspaceState):
                                if (reader.Read())
                                {
                                    options.Converters.Add(TagHelperDescriptorJsonConverter2.Instance);
                                    projectWorkspaceState = JsonSerializer.Deserialize<ProjectWorkspaceState>(ref reader, options);
                                }
                                break;
                            case nameof(FullProjectSnapshotHandle.Documents):
                                if (reader.Read())
                                {
                                    documents = JsonSerializer.Deserialize<DocumentSnapshotHandle[]>(ref reader);
                                }
                                break;
                        }

                        break;
                    //case JsonTokenType.EndObject:
                    //    return;
                }
            }

            // We need to add a serialization format to the project response to indicate that this version of the code is compatible with what's being serialized.
            // This scenario typically happens when a user has an incompatible serialized project snapshot but is using the latest Razor bits.

            if (string.IsNullOrEmpty(serializationFormat) || serializationFormat != ProjectSerializationFormat.Version)
            {
                // Unknown serialization format.
                return null;
            }

            return new FullProjectSnapshotHandle(filePath, configuration, rootNamespace, projectWorkspaceState, documents);
        }

        public override void Write(Utf8JsonWriter writer, FullProjectSnapshotHandle value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName(nameof(FullProjectSnapshotHandle.FilePath));
            writer.WriteStringValue(value.FilePath);

            if (value.Configuration == null)
            {
                writer.WritePropertyName(nameof(FullProjectSnapshotHandle.Configuration));
                writer.WriteNullValue();
            }
            else
            {
                writer.WritePropertyName(nameof(FullProjectSnapshotHandle.Configuration));
                RazorConfigurationJsonConverter2.Instance.Write(writer, value.Configuration, options);
            }

            if (value.ProjectWorkspaceState == null)
            {
                writer.WritePropertyName(nameof(FullProjectSnapshotHandle.ProjectWorkspaceState));
                writer.WriteNullValue();
            }
            else
            {
                options.Converters.Add(TagHelperDescriptorJsonConverter2.Instance);
                writer.WritePropertyName(nameof(FullProjectSnapshotHandle.ProjectWorkspaceState));
                JsonSerializer.Serialize(writer, value.ProjectWorkspaceState, options);
            }

            writer.WritePropertyName(nameof(FullProjectSnapshotHandle.RootNamespace));
            writer.WriteStringValue(value.RootNamespace);

            writer.WritePropertyName(nameof(FullProjectSnapshotHandle.Documents));
            JsonSerializer.Serialize(writer, value.Documents);

            writer.WritePropertyName(SerializationFormatPropertyName);
            writer.WriteStringValue(ProjectSerializationFormat.Version);

            writer.WriteEndObject();
        }
    }

    internal class RazorConfigurationJsonConverter2 : JsonConverter<RazorConfiguration>
    {
        public static readonly RazorConfigurationJsonConverter2 Instance = new RazorConfigurationJsonConverter2();

        public override bool CanConvert(Type objectType)
        {
            return typeof(RazorConfiguration).IsAssignableFrom(objectType);
        }

        public override RazorConfiguration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            var configurationName = reader.ReadNextStringProperty(nameof(RazorConfiguration.ConfigurationName));
            var languageVersionValue = reader.ReadNextStringProperty(nameof(RazorConfiguration.LanguageVersion));

            if (reader.TokenType != JsonTokenType.StartArray || !reader.Read())
            {
                return null;
            }

            var extensions = new RazorExtension[] { RazorExtensionJsonConverter2.Instance.Read(ref reader, typeof(RazorExtension), options) };

            if (reader.TokenType != JsonTokenType.EndArray || !reader.Read())
            {
                return null;
            }


            if (!RazorLanguageVersion.TryParse(languageVersionValue, out var languageVersion))
            {
                languageVersion = RazorLanguageVersion.Version_2_1;
            }

            return RazorConfiguration.Create(languageVersion, configurationName, extensions);
        }

        public override void Write(Utf8JsonWriter writer, RazorConfiguration value, JsonSerializerOptions options)
        {
            var configuration = (RazorConfiguration)value;

            writer.WriteStartObject();

            writer.WriteString(nameof(RazorConfiguration.ConfigurationName), configuration.ConfigurationName);

            writer.WritePropertyName(nameof(RazorConfiguration.LanguageVersion));
            if (configuration.LanguageVersion == RazorLanguageVersion.Experimental)
            {
                writer.WriteStringValue("Experimental");
            }
            else
            {
                writer.WriteStringValue(configuration.LanguageVersion.ToString());
            }

            writer.WritePropertyName(nameof(RazorConfiguration.Extensions));
            writer.WriteStartArray();

            foreach (var extension in configuration.Extensions)
            {
                RazorExtensionJsonConverter2.Instance.Write(writer, extension, options);
            }

            writer.WriteEndArray();

            writer.WriteEndObject();
        }
    }


    internal class RazorExtensionJsonConverter2 : JsonConverter<RazorExtension>
    {
        public static readonly RazorExtensionJsonConverter2 Instance = new RazorExtensionJsonConverter2();

        public override bool CanConvert(Type objectType)
        {
            return typeof(RazorExtension).IsAssignableFrom(objectType);
        }

        public override RazorExtension Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            var extensionName = reader.ReadNextStringProperty(nameof(RazorExtension.ExtensionName));

            return new SerializedRazorExtension(extensionName);
        }

        public override void Write(Utf8JsonWriter writer, RazorExtension value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(RazorExtension.ExtensionName), value.ExtensionName);
            writer.WriteEndObject();
        }
    }


    internal static class Utf8JsonReaderExtensions
    {
        //public static bool ReadTokenAndAdvance(this Utf8JsonReader reader, JsonTokenType expectedTokenType, out object value)
        //{
        //    value = reader.Value;
        //    return reader.TokenType == expectedTokenType && reader.Read();
        //}

        //public static void ReadProperties(this Utf8JsonReader reader, Action<string> onProperty)
        //{
        //    while (reader.Read())
        //    {
        //        switch (reader.TokenType)
        //        {
        //            case JsonTokenType.PropertyName:
        //                var propertyName = reader.GetString();
        //                onProperty(propertyName);
        //                break;
        //            case JsonTokenType.EndObject:
        //                return;
        //        }
        //    }
        //}

        public static string ReadNextStringProperty(this Utf8JsonReader reader, string propertyName)
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        Debug.Assert(reader.GetString() == propertyName);
                        if (reader.Read())
                        {
                            var value = reader.GetString();
                            return value;
                        }
                        else
                        {
                            return null;
                        }
                }
            }

            // throw new JsonSerializationException($"Could not find string property '{propertyName}'.");
            return null;
        }
    }

    internal class SerializedRazorExtension : RazorExtension
    {
        public SerializedRazorExtension(string extensionName)
        {
            if (extensionName == null)
            {
                throw new ArgumentNullException(nameof(extensionName));
            }

            ExtensionName = extensionName;
        }

        public override string ExtensionName { get; }
    }

    internal class TagHelperDescriptorJsonConverter2 : JsonConverter<TagHelperDescriptor>
    {
        public static readonly TagHelperDescriptorJsonConverter2 Instance = new TagHelperDescriptorJsonConverter2();

        public override bool CanConvert(Type objectType)
        {
            return typeof(TagHelperDescriptor).IsAssignableFrom(objectType);
        }

        public override TagHelperDescriptor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            // Required tokens (order matters)
            var descriptorKind = reader.ReadNextStringProperty(nameof(TagHelperDescriptor.Kind));
            var typeName = reader.ReadNextStringProperty(nameof(TagHelperDescriptor.Name));
            var assemblyName = reader.ReadNextStringProperty(nameof(TagHelperDescriptor.AssemblyName));
            var builder = TagHelperDescriptorBuilder.Create(descriptorKind, typeName, assemblyName);


            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        var propertyName = reader.GetString();

                        switch (propertyName)
                        {
                            case nameof(TagHelperDescriptor.Documentation):
                                if (reader.Read())
                                {
                                    var documentation = (string)reader.Value;
                                    builder.Documentation = documentation;
                                }
                                break;
                            case nameof(TagHelperDescriptor.TagOutputHint):
                                if (reader.Read())
                                {
                                    var tagOutputHint = (string)reader.Value;
                                    builder.TagOutputHint = tagOutputHint;
                                }
                                break;
                            case nameof(TagHelperDescriptor.CaseSensitive):
                                if (reader.Read())
                                {
                                    var caseSensitive = (bool)reader.Value;
                                    builder.CaseSensitive = caseSensitive;
                                }
                                break;
                            case nameof(TagHelperDescriptor.TagMatchingRules):
                                ReadTagMatchingRules(reader, builder);
                                break;
                            case nameof(TagHelperDescriptor.BoundAttributes):
                                ReadBoundAttributes(reader, builder);
                                break;
                            case nameof(TagHelperDescriptor.AllowedChildTags):
                                ReadAllowedChildTags(reader, builder);
                                break;
                            case nameof(TagHelperDescriptor.Diagnostics):
                                ReadDiagnostics(reader, builder.Diagnostics);
                                break;
                            case nameof(TagHelperDescriptor.Metadata):
                                ReadMetadata(reader, builder.Metadata);
                                break;
                        }
                }
            }

            return builder.Build();
        }

        public override void Write(Utf8JsonWriter writer, TagHelperDescriptor value, JsonSerializerOptions options)
        {
            var tagHelper = (TagHelperDescriptor)value;

            writer.WriteStartObject();

            writer.WritePropertyName(nameof(TagHelperDescriptor.Kind));
            writer.WriteStringValue(tagHelper.Kind);

            writer.WritePropertyName(nameof(TagHelperDescriptor.Name));
            writer.WriteStringValue(tagHelper.Name);

            writer.WritePropertyName(nameof(TagHelperDescriptor.AssemblyName));
            writer.WriteStringValue(tagHelper.AssemblyName);

            if (tagHelper.Documentation != null)
            {
                writer.WritePropertyName(nameof(TagHelperDescriptor.Documentation));
                writer.WriteStringValue(tagHelper.Documentation);
            }

            if (tagHelper.TagOutputHint != null)
            {
                writer.WritePropertyName(nameof(TagHelperDescriptor.TagOutputHint));
                writer.WriteStringValue(tagHelper.TagOutputHint);
            }

            writer.WritePropertyName(nameof(TagHelperDescriptor.CaseSensitive));
            writer.WriteBooleanValue(tagHelper.CaseSensitive);

            writer.WritePropertyName(nameof(TagHelperDescriptor.TagMatchingRules));
            writer.WriteStartArray();
            foreach (var ruleDescriptor in tagHelper.TagMatchingRules)
            {
                WriteTagMatchingRule(writer, ruleDescriptor, options);
            }
            writer.WriteEndArray();

            if (tagHelper.BoundAttributes != null && tagHelper.BoundAttributes.Count > 0)
            {
                writer.WritePropertyName(nameof(TagHelperDescriptor.BoundAttributes));
                writer.WriteStartArray();
                foreach (var boundAttribute in tagHelper.BoundAttributes)
                {
                    WriteBoundAttribute(writer, boundAttribute, options);
                }
                writer.WriteEndArray();
            }

            if (tagHelper.AllowedChildTags != null && tagHelper.AllowedChildTags.Count > 0)
            {
                writer.WritePropertyName(nameof(TagHelperDescriptor.AllowedChildTags));
                writer.WriteStartArray();
                foreach (var allowedChildTag in tagHelper.AllowedChildTags)
                {
                    WriteAllowedChildTags(writer, allowedChildTag, serializer);
                }
                writer.WriteEndArray();
            }

            if (tagHelper.Diagnostics != null && tagHelper.Diagnostics.Count > 0)
            {
                writer.WritePropertyName(nameof(TagHelperDescriptor.Diagnostics));
                serializer.Serialize(writer, tagHelper.Diagnostics);
            }

            writer.WritePropertyName(nameof(TagHelperDescriptor.Metadata));
            WriteMetadata(writer, tagHelper.Metadata);

            writer.WriteEndObject();
        }

        private static void WriteAllowedChildTags(Utf8JsonWriter writer, AllowedChildTagDescriptor allowedChildTag, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName(nameof(AllowedChildTagDescriptor.Name));
            writer.WriteStringValue(allowedChildTag.Name);

            writer.WritePropertyName(nameof(AllowedChildTagDescriptor.DisplayName));
            writer.WriteStringValue(allowedChildTag.DisplayName);

            writer.WritePropertyName(nameof(AllowedChildTagDescriptor.Diagnostics));
            serializer.Serialize(writer, allowedChildTag.Diagnostics);

            writer.WriteEndObject();
        }

        private static void WriteBoundAttribute(Utf8JsonWriter writer, BoundAttributeDescriptor boundAttribute, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName(nameof(BoundAttributeDescriptor.Kind));
            writer.WriteStringValue(boundAttribute.Kind);

            writer.WritePropertyName(nameof(BoundAttributeDescriptor.Name));
            writer.WriteStringValue(boundAttribute.Name);

            writer.WritePropertyName(nameof(BoundAttributeDescriptor.TypeName));
            writer.WriteStringValue(boundAttribute.TypeName);

            if (boundAttribute.IsEnum)
            {
                writer.WritePropertyName(nameof(BoundAttributeDescriptor.IsEnum));
                writer.WriteBooleanValue(boundAttribute.IsEnum);
            }

            if (boundAttribute.IndexerNamePrefix != null)
            {
                writer.WritePropertyName(nameof(BoundAttributeDescriptor.IndexerNamePrefix));
                writer.WriteStringValue(boundAttribute.IndexerNamePrefix);
            }

            if (boundAttribute.IndexerTypeName != null)
            {
                writer.WritePropertyName(nameof(BoundAttributeDescriptor.IndexerTypeName));
                writer.WriteStringValue(boundAttribute.IndexerTypeName);
            }

            if (boundAttribute.Documentation != null)
            {
                writer.WritePropertyName(nameof(BoundAttributeDescriptor.Documentation));
                writer.WriteStringValue(boundAttribute.Documentation);
            }

            if (boundAttribute.Diagnostics != null && boundAttribute.Diagnostics.Count > 0)
            {
                writer.WritePropertyName(nameof(BoundAttributeDescriptor.Diagnostics));
                serializer.Serialize(writer, boundAttribute.Diagnostics);
            }

            writer.WritePropertyName(nameof(BoundAttributeDescriptor.Metadata));
            WriteMetadata(writer, boundAttribute.Metadata);

            if (boundAttribute.BoundAttributeParameters != null && boundAttribute.BoundAttributeParameters.Count > 0)
            {
                writer.WritePropertyName(nameof(BoundAttributeDescriptor.BoundAttributeParameters));
                writer.WriteStartArray();
                foreach (var boundAttributeParameter in boundAttribute.BoundAttributeParameters)
                {
                    WriteBoundAttributeParameter(writer, boundAttributeParameter, serializer);
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        private static void WriteBoundAttributeParameter(Utf8JsonWriter writer, BoundAttributeParameterDescriptor boundAttributeParameter, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName(nameof(BoundAttributeParameterDescriptor.Name));
            writer.WriteStringValue(boundAttributeParameter.Name);

            writer.WritePropertyName(nameof(BoundAttributeParameterDescriptor.TypeName));
            writer.WriteStringValue(boundAttributeParameter.TypeName);

            if (boundAttributeParameter.IsEnum != default)
            {
                writer.WritePropertyName(nameof(BoundAttributeParameterDescriptor.IsEnum));
                writer.WriteBooleanValue(boundAttributeParameter.IsEnum);
            }

            if (boundAttributeParameter.Documentation != null)
            {
                writer.WritePropertyName(nameof(BoundAttributeParameterDescriptor.Documentation));
                writer.WriteStringValue(boundAttributeParameter.Documentation);
            }

            if (boundAttributeParameter.Diagnostics != null && boundAttributeParameter.Diagnostics.Count > 0)
            {
                writer.WritePropertyName(nameof(BoundAttributeParameterDescriptor.Diagnostics));
                serializer.Serialize(writer, boundAttributeParameter.Diagnostics);
            }

            writer.WritePropertyName(nameof(BoundAttributeParameterDescriptor.Metadata));
            WriteMetadata(writer, boundAttributeParameter.Metadata);

            writer.WriteEndObject();
        }

        private static void WriteMetadata(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> metadata)
        {
            writer.WriteStartObject();
            foreach (var kvp in metadata)
            {
                writer.WritePropertyName(kvp.Key);
                writer.WriteStringValue(kvp.Value);
            }
            writer.WriteEndObject();
        }

        private static void WriteTagMatchingRule(Utf8JsonWriter writer, TagMatchingRuleDescriptor ruleDescriptor, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName(nameof(TagMatchingRuleDescriptor.TagName));
            writer.WriteStringValue(ruleDescriptor.TagName);

            if (ruleDescriptor.ParentTag != null)
            {
                writer.WritePropertyName(nameof(TagMatchingRuleDescriptor.ParentTag));
                writer.WriteStringValue(ruleDescriptor.ParentTag);
            }

            if (ruleDescriptor.TagStructure != default)
            {
                writer.WritePropertyName(nameof(TagMatchingRuleDescriptor.TagStructure));
                writer.WriteNumberValue((int)ruleDescriptor.TagStructure);
            }

            if (ruleDescriptor.Attributes != null && ruleDescriptor.Attributes.Count > 0)
            {
                writer.WritePropertyName(nameof(TagMatchingRuleDescriptor.Attributes));
                writer.WriteStartArray();
                foreach (var requiredAttribute in ruleDescriptor.Attributes)
                {
                    WriteRequiredAttribute(writer, requiredAttribute, options);
                }
                writer.WriteEndArray();
            }

            if (ruleDescriptor.Diagnostics != null && ruleDescriptor.Diagnostics.Count > 0)
            {
                writer.WritePropertyName(nameof(TagMatchingRuleDescriptor.Diagnostics));
                serializer.Serialize(writer, ruleDescriptor.Diagnostics);
            }

            writer.WriteEndObject();
        }

        private static void WriteRequiredAttribute(Utf8JsonWriter writer, RequiredAttributeDescriptor requiredAttribute, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName(nameof(RequiredAttributeDescriptor.Name));
            writer.WriteStringValue(requiredAttribute.Name);

            if (requiredAttribute.NameComparison != default)
            {
                writer.WritePropertyName(nameof(RequiredAttributeDescriptor.NameComparison));
                writer.WriteNumberValue((int)requiredAttribute.NameComparison);
            }

            if (requiredAttribute.Value != null)
            {
                writer.WritePropertyName(nameof(RequiredAttributeDescriptor.Value));
                writer.WriteStringValue(requiredAttribute.Value);
            }

            if (requiredAttribute.ValueComparison != default)
            {
                writer.WritePropertyName(nameof(RequiredAttributeDescriptor.ValueComparison));
                writer.WriteStringValue((int)requiredAttribute.ValueComparison);
            }

            if (requiredAttribute.Diagnostics != null && requiredAttribute.Diagnostics.Count > 0)
            {
                writer.WritePropertyName(nameof(RequiredAttributeDescriptor.Diagnostics));
                serializer.Serialize(writer, requiredAttribute.Diagnostics);
            }

            if (requiredAttribute.Metadata != null && requiredAttribute.Metadata.Count > 0)
            {
                writer.WritePropertyName(nameof(RequiredAttributeDescriptor.Metadata));
                WriteMetadata(writer, requiredAttribute.Metadata);
            }

            writer.WriteEndObject();
        }

        private static void ReadBoundAttributes(ref Utf8JsonReader reader, TagHelperDescriptorBuilder builder)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                return;
            }

            do
            {
                ReadBoundAttribute(ref reader, builder);
            } while (reader.TokenType != JsonTokenType.EndArray);
        }

        private static void ReadBoundAttribute(ref Utf8JsonReader readerRef, TagHelperDescriptorBuilder builder)
        {
            var reader = readerRef;
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return;
            }

            builder.BindAttribute(attribute =>
            {
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                            var propertyName = reader.GetString();
                            switch (propertyName)
                            {
                                case nameof(BoundAttributeDescriptor.Name):
                                    if (reader.Read())
                                    {
                                        var name = (string)reader.Value;
                                        attribute.Name = name;
                                    }
                                    break;
                                case nameof(BoundAttributeDescriptor.TypeName):
                                    if (reader.Read())
                                    {
                                        var typeName = (string)reader.Value;
                                        attribute.TypeName = typeName;
                                    }
                                    break;
                                case nameof(BoundAttributeDescriptor.Documentation):
                                    if (reader.Read())
                                    {
                                        var documentation = (string)reader.Value;
                                        attribute.Documentation = documentation;
                                    }
                                    break;
                                case nameof(BoundAttributeDescriptor.IndexerNamePrefix):
                                    if (reader.Read())
                                    {
                                        var indexerNamePrefix = (string)reader.Value;
                                        if (indexerNamePrefix != null)
                                        {
                                            attribute.IsDictionary = true;
                                            attribute.IndexerAttributeNamePrefix = indexerNamePrefix;
                                        }
                                    }
                                    break;
                                case nameof(BoundAttributeDescriptor.IndexerTypeName):
                                    if (reader.Read())
                                    {
                                        var indexerTypeName = (string)reader.Value;
                                        if (indexerTypeName != null)
                                        {
                                            attribute.IsDictionary = true;
                                            attribute.IndexerValueTypeName = indexerTypeName;
                                        }
                                    }
                                    break;
                                case nameof(BoundAttributeDescriptor.IsEnum):
                                    if (reader.Read())
                                    {
                                        var isEnum = (bool)reader.Value;
                                        attribute.IsEnum = isEnum;
                                    }
                                    break;
                                case nameof(BoundAttributeDescriptor.BoundAttributeParameters):
                                    ReadBoundAttributeParameters(reader, attribute);
                                    break;
                                case nameof(BoundAttributeDescriptor.Diagnostics):
                                    ReadDiagnostics(reader, attribute.Diagnostics);
                                    break;
                                case nameof(BoundAttributeDescriptor.Metadata):
                                    ReadMetadata(reader, attribute.Metadata);
                                    break;
                            }
                    }
                }
            });
        }

        private static void ReadBoundAttributeParameters(ref Utf8JsonReader reader, BoundAttributeDescriptorBuilder builder)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                return;
            }

            do
            {
                ReadBoundAttributeParameter(reader, builder);
            } while (reader.TokenType != JsonTokenType.EndArray);
        }

        private static void ReadBoundAttributeParameter(ref Utf8JsonReader reader, BoundAttributeDescriptorBuilder builder)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return;
            }

            builder.BindAttributeParameter(parameter =>
            {
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                            var propertyName = reader.GetString();
                            switch (propertyName)
                            {
                                case nameof(BoundAttributeParameterDescriptor.Name):
                                    if (reader.Read())
                                    {
                                        var name = (string)reader.Value;
                                        parameter.Name = name;
                                    }
                                    break;
                                case nameof(BoundAttributeParameterDescriptor.TypeName):
                                    if (reader.Read())
                                    {
                                        var typeName = (string)reader.Value;
                                        parameter.TypeName = typeName;
                                    }
                                    break;
                                case nameof(BoundAttributeParameterDescriptor.IsEnum):
                                    if (reader.Read())
                                    {
                                        var isEnum = (bool)reader.Value;
                                        parameter.IsEnum = isEnum;
                                    }
                                    break;
                                case nameof(BoundAttributeParameterDescriptor.Documentation):
                                    if (reader.Read())
                                    {
                                        var documentation = (string)reader.Value;
                                        parameter.Documentation = documentation;
                                    }
                                    break;
                                case nameof(BoundAttributeParameterDescriptor.Metadata):
                                    ReadMetadata(reader, parameter.Metadata);
                                    break;
                                case nameof(BoundAttributeParameterDescriptor.Diagnostics):
                                    ReadDiagnostics(reader, parameter.Diagnostics);
                                    break;
                            }
                    }
                }
            });
        }

        private static void ReadTagMatchingRules(ref Utf8JsonReader reader, TagHelperDescriptorBuilder builder)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                return;
            }

            do
            {
                ReadTagMatchingRule(ref reader, builder);
            } while (reader.TokenType != JsonTokenType.EndArray);
        }

        private static void ReadTagMatchingRule(ref Utf8JsonReader reader, TagHelperDescriptorBuilder builder)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return;
            }

            builder.TagMatchingRule(rule =>
            {
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                            var propertyName = reader.GetString();
                            switch (propertyName)
                            {
                                case nameof(TagMatchingRuleDescriptor.TagName):
                                    if (reader.Read())
                                    {
                                        var tagName = (string)reader.Value;
                                        rule.TagName = tagName;
                                    }
                                    break;
                                case nameof(TagMatchingRuleDescriptor.ParentTag):
                                    if (reader.Read())
                                    {
                                        var parentTag = (string)reader.Value;
                                        rule.ParentTag = parentTag;
                                    }
                                    break;
                                case nameof(TagMatchingRuleDescriptor.TagStructure):
                                    rule.TagStructure = (TagStructure)reader.ReadAsInt32();
                                    break;
                                case nameof(TagMatchingRuleDescriptor.Attributes):
                                    ReadRequiredAttributeValues(reader, rule);
                                    break;
                                case nameof(TagMatchingRuleDescriptor.Diagnostics):
                                    ReadDiagnostics(reader, rule.Diagnostics);
                                    break;
                            }
                    }
                }
            });
        }

        private static void ReadRequiredAttributeValues(ref Utf8JsonReader reader, TagMatchingRuleDescriptorBuilder builder)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                return;
            }

            do
            {
                ReadRequiredAttribute(ref reader, builder);
            } while (reader.TokenType != JsonTokenType.EndArray);
        }

        private static void ReadRequiredAttribute(ref Utf8JsonReader reader, TagMatchingRuleDescriptorBuilder builder)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return;
            }

            builder.Attribute(attribute =>
            {
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                            var propertyName = reader.GetString();
                            switch (propertyName)
                            {
                                case nameof(RequiredAttributeDescriptor.Name):
                                    if (reader.Read())
                                    {
                                        var name = (string)reader.GetString();
                                        attribute.Name = name;
                                    }
                                    break;
                                case nameof(RequiredAttributeDescriptor.NameComparison):
                                    var nameComparison = (RequiredAttributeDescriptor.NameComparisonMode)reader.ReadAsInt32();
                                    attribute.NameComparisonMode = nameComparison;
                                    break;
                                case nameof(RequiredAttributeDescriptor.Value):
                                    if (reader.Read())
                                    {
                                        var value = (string)reader.GetString();
                                        attribute.Value = value;
                                    }
                                    break;
                                case nameof(RequiredAttributeDescriptor.ValueComparison):
                                    var valueComparison = (RequiredAttributeDescriptor.ValueComparisonMode)reader.ReadAsInt32();
                                    attribute.ValueComparisonMode = valueComparison;
                                    break;
                                case nameof(RequiredAttributeDescriptor.Diagnostics):
                                    ReadDiagnostics(reader, attribute.Diagnostics);
                                    break;
                                case nameof(RequiredAttributeDescriptor.Metadata):
                                    ReadMetadata(reader, attribute.Metadata);
                                    break;
                            }
                    }
                }
            });
        }

        private static void ReadAllowedChildTags(ref Utf8JsonReader reader, TagHelperDescriptorBuilder builder)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                return;
            }

            do
            {
                ReadAllowedChildTag(ref reader, builder);
            } while (reader.TokenType != JsonTokenType.EndArray);
        }

        private static void ReadAllowedChildTag(ref Utf8JsonReader reader, TagHelperDescriptorBuilder builder)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return;
            }

            builder.AllowChildTag(childTag =>
            {
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                            var propertyName = reader.GetString();
                            switch (propertyName)
                            {
                                case nameof(AllowedChildTagDescriptor.Name):
                                    if (reader.Read())
                                    {
                                        var name = (string)reader.GetString();
                                        childTag.Name = name;
                                    }
                                    break;
                                case nameof(AllowedChildTagDescriptor.DisplayName):
                                    if (reader.Read())
                                    {
                                        var displayName = (string)reader.GetString();
                                        childTag.DisplayName = displayName;
                                    }
                                    break;
                                case nameof(AllowedChildTagDescriptor.Diagnostics):
                                    ReadDiagnostics(reader, childTag.Diagnostics);
                                    break;
                            }
                    }
                }
            });
        }

        private static void ReadMetadata(ref Utf8JsonReader reader, IDictionary<string, string> metadata)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return;
            }

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        var propertyName = reader.GetString();
                        if (reader.Read())
                        {
                            var value = (string)reader.GetString();
                            metadata[propertyName] = value;
                        }
                        break;
                }
            }
        }

        private static void ReadDiagnostics(ref Utf8JsonReader reader, RazorDiagnosticCollection diagnostics)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                return;
            }

            do
            {
                ReadDiagnostic(ref reader, diagnostics);
            } while (reader.TokenType != JsonTokenType.EndArray);
        }

        private static void ReadDiagnostic(ref Utf8JsonReader reader, RazorDiagnosticCollection diagnostics)
        {
            if (!reader.Read())
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return;
            }

            string id = default;
            int severity = default;
            string message = default;
            SourceSpan sourceSpan = default;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        var propertyName = reader.GetString();
                        switch (propertyName)
                        {
                            case nameof(RazorDiagnostic.Id):
                                if (reader.Read())
                                {
                                    id = (string)reader.GetString();
                                }
                                break;
                            case nameof(RazorDiagnostic.Severity):
                                severity = reader.GetInt32();
                                break;
                            case "Message":
                                if (reader.Read())
                                {
                                    message = (string)reader.GetString();
                                }
                                break;
                            case nameof(RazorDiagnostic.Span):
                                sourceSpan = ReadSourceSpan(ref reader);
                                break;
                        }
                        break;
                }
            }

            var descriptor = new RazorDiagnosticDescriptor(id, () => message, (RazorDiagnosticSeverity)severity);

            var diagnostic = RazorDiagnostic.Create(descriptor, sourceSpan);
            diagnostics.Add(diagnostic);
        }

        private static SourceSpan ReadSourceSpan(ref Utf8JsonReader reader)
        {
            if (!reader.Read())
            {
                return SourceSpan.Undefined;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return SourceSpan.Undefined;
            }

            string filePath = default;
            int absoluteIndex = default;
            int lineIndex = default;
            int characterIndex = default;
            int length = default;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        var propertyName = reader.GetString();
                        switch (propertyName)
                        {
                            case nameof(SourceSpan.FilePath):
                                if (reader.Read())
                                {
                                    filePath = (string)reader.GetString();
                                }
                                break;
                            case nameof(SourceSpan.AbsoluteIndex):
                                absoluteIndex = reader.GetInt32();
                                break;
                            case nameof(SourceSpan.LineIndex):
                                lineIndex = reader.GetInt32();
                                break;
                            case nameof(SourceSpan.CharacterIndex):
                                characterIndex = reader.GetInt32();
                                break;
                            case nameof(SourceSpan.Length):
                                length = reader.GetInt32();
                                break;
                        }
                        break;
                }
            }

            var sourceSpan = new SourceSpan(filePath, absoluteIndex, lineIndex, characterIndex, length);
            return sourceSpan;
        }
    }
}