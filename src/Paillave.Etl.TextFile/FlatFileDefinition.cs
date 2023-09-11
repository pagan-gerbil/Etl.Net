using Paillave.Etl.Core;
using Paillave.Etl.Core.Mapping;
using Paillave.Etl.Core.Mapping.Visitors;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Paillave.Etl.TextFile
{
    public static class FlatFileDefinition
    {
        public static FlatFileDefinition<T> Create<T>(Expression<Func<IFieldMapper, T>> expression) => new FlatFileDefinition<T>().WithMap(expression);
    }
    public class FlatFileDefinition<T>
    {
        public Encoding Encoding { get; private set; } = null;
        public bool HasColumnHeader => _fieldDefinitions.Any(i => !string.IsNullOrWhiteSpace(i.ColumnName));
        private IList<FlatFileFieldDefinition> _fieldDefinitions = new List<FlatFileFieldDefinition>();
        private ILineSplitter _lineSplitter = new ColumnSeparatorLineSplitter();
        private CultureInfo _cultureInfo = CultureInfo.CurrentCulture;
        private bool _respectHeaderCase = false;

        public int FirstLinesToIgnore { get; private set; }
        private IEnumerable<string> GetDefaultColumnNames()
        {
            return _fieldDefinitions.Select((i, idx) => new { Name = i.ColumnName ?? i.PropertyInfo.Name, DefinedPosition = i.Position, FallbackPosition = idx })
                .OrderBy(i => i.DefinedPosition)
                .ThenBy(i => i.FallbackPosition)
                .Select(i => i.Name);
        }
        private void SetFieldDefinition(FlatFileFieldDefinition fieldDefinition)
        {
            var existingFieldDefinition = _fieldDefinitions.FirstOrDefault(i => i.PropertyInfo.Name == fieldDefinition.PropertyInfo.Name);
            if (existingFieldDefinition == null)
            {
                if (fieldDefinition.Position == null)
                    fieldDefinition.Position = (_fieldDefinitions.Max(i => i.Position) ?? 0) + 1;
                _fieldDefinitions.Add(fieldDefinition);
            }
            else
            {
                if (fieldDefinition.ColumnName != null) existingFieldDefinition.ColumnName = fieldDefinition.ColumnName;
                if (fieldDefinition.Position != null) existingFieldDefinition.Position = fieldDefinition.Position;
            }
        }
        public FlatFileDefinition<T> IgnoreFirstLines(int firstLinesToIgnore)
        {
            FirstLinesToIgnore = firstLinesToIgnore;
            return this;
        }
        public FlatFileDefinition<T> WithMap(Expression<Func<IFieldMapper, T>> expression)
        {
            MapperVisitor vis = new MapperVisitor();
            vis.Visit(expression);
            foreach (var item in vis.MappingSetters)
            {
                this.SetFieldDefinition(new FlatFileFieldDefinition
                {
                    ColumnName = item.ColumnName,
                    Position = item.ColumnIndex,
                    PropertyInfo = item.TargetPropertyInfo,
                    CultureInfo = item.CreateCultureInfo(),
                    ForSourceName = item.ForSourceName,
                    ForLineNumber = item.ForLineNumber,
                    ForRowGuid = item.ForRowGuid,
                    FalseValues = item.FalseValues,
                    TrueValues = item.TrueValues
                });
            }
            if (vis.MappingSetters.Any(i => i.Size.HasValue))
            {
                if (!vis.MappingSetters.All(i => i.Size.HasValue))
                    throw new InvalidOperationException(
                        $"if a size is given, all sizes must be given: missing size for columns with indexes: {string.Join(", ", vis.MappingSetters.Where(i => !i.Size.HasValue).Select(i => i.ColumnIndex))}.");

                this.HasFixedColumnWidth(vis.MappingSetters.OrderBy(i => i.ColumnIndex).Select(i => i.Size.Value).ToArray());
            }
            return this;
        }
        private FlatFileDefinition<T> SetDefaultMapping(bool withColumnHeader = true, CultureInfo cultureInfo = null)
        {
            foreach (var item in typeof(T).GetProperties().Select((propertyInfo, index) => new { propertyInfo = propertyInfo, Position = index }))
            {
                SetFieldDefinition(new FlatFileFieldDefinition
                {
                    CultureInfo = cultureInfo,
                    ColumnName = withColumnHeader ? item.propertyInfo.Name : null,
                    Position = item.Position,
                    PropertyInfo = item.propertyInfo
                });
            }
            return this;
        }
        public LineSerializer<T> GetSerializer(string headerLine)
        {
            return GetSerializer(_lineSplitter.Split(headerLine));
        }
        public LineSerializer<T> GetSerializer(IEnumerable<string> columnNames = null)
        {
            if ((_fieldDefinitions?.Count ?? 0) == 0) SetDefaultMapping();
            if (columnNames == null) columnNames = GetDefaultColumnNames().ToList();
            var fileNamePropertyNames = _fieldDefinitions.Where(i => i.ForSourceName).Select(i => i.PropertyInfo.Name).ToList();
            var rowNumberPropertyNames = _fieldDefinitions.Where(i => i.ForLineNumber).Select(i => i.PropertyInfo.Name).ToList();
            var rowGuidPropertyNames = _fieldDefinitions.Where(i => i.ForRowGuid).Select(i => i.PropertyInfo.Name).ToList();
            if (this.HasColumnHeader)
            {
                var indexToPropertySerializerDictionaryTmp = _fieldDefinitions.Join(
                    columnNames.Select((ColumnName, Position) => new { ColumnName, Position }),
                    i => _respectHeaderCase ? i?.ColumnName?.Trim() : i?.ColumnName?.ToLowerInvariant()?.Trim(),
                    i => _respectHeaderCase ? i.ColumnName.Trim() : i.ColumnName.ToLowerInvariant().Trim(),
                    (fd, po) => new
                    {
                        Position = po.Position,
                        PropertySerializer = new FlatFilePropertySerializer(fd.PropertyInfo, fd.CultureInfo ?? _cultureInfo, fd.TrueValues, fd.FalseValues, po.ColumnName)
                    });
                var indexToPropertySerializerDictionary = indexToPropertySerializerDictionaryTmp
                    .ToDictionary(i => i.Position, i => i.PropertySerializer);
                return new LineSerializer<T>(_lineSplitter, indexToPropertySerializerDictionary, fileNamePropertyNames, rowNumberPropertyNames, rowGuidPropertyNames);
            }
            else
            {
                var indexToPropertySerializerDictionary = _fieldDefinitions
                    .OrderBy(i => i.Position)
                    .Select((fd, idx) => new
                    {
                        Position = idx,
                        PropertySerializer = new FlatFilePropertySerializer(fd.PropertyInfo, fd.CultureInfo ?? _cultureInfo, fd.TrueValues, fd.FalseValues, $"<{idx}>")
                    })
                    .ToDictionary(i => i.Position, i => i.PropertySerializer);
                return new LineSerializer<T>(_lineSplitter, indexToPropertySerializerDictionary, fileNamePropertyNames, rowNumberPropertyNames, rowGuidPropertyNames);
            }
        }
        public string GenerateDefaultHeaderLine()
        {
            return _lineSplitter.Join(GetDefaultColumnNames());
        }
        public FlatFileDefinition<T> IsColumnSeparated(char separator = ';', char textDelimiter = '"')
        {
            this._lineSplitter = new ColumnSeparatorLineSplitter(separator, textDelimiter);
            return this;
        }
        public FlatFileDefinition<T> RespectHeaderCase(bool respectHeaderCase = true)
        {
            this._respectHeaderCase = respectHeaderCase;
            return this;
        }
        public FlatFileDefinition<T> HasFixedColumnWidth(params int[] columnSizes)
        {
            this._lineSplitter = new FixedColumnWidthLineSplitter(columnSizes);
            return this;
        }
        public FlatFileDefinition<T> WithCultureInfo(CultureInfo cultureInfo)
        {
            this._cultureInfo = cultureInfo;
            return this;
        }
        public FlatFileDefinition<T> WithEncoding(Encoding encoding)
        {
            this.Encoding = encoding;
            return this;
        }
        public FlatFileDefinition<T> WithCultureInfo(string name)
        {
            this._cultureInfo = CultureInfo.GetCultureInfo(name);
            return this;
        }
    }
}
