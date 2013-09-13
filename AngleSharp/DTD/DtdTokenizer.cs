﻿using AngleSharp.Xml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace AngleSharp.DTD
{
    /// <summary>
    /// The tokenizer class for Document Type Definitions.
    /// </summary>
    [DebuggerStepThrough]
    sealed class DtdTokenizer : BaseTokenizer
    {
        #region Constants

        const String ENTITY = "ENTITY";
        const String ELEMENT = "ELEMENT";
        const String NOTATION = "NOTATION";
        const String ATTLIST = "ATTLIST";
        const String EMPTY = "EMPTY";
        const String ANY = "ANY";
        const String PCDATA = "#PCDATA";
        const String NDATA = "NDATA";
        const String CDATA = "CDATA";
        const String ID = "ID";
        const String IDREF = "IDREF";
        const String IDREFS = "IDREFS";
        const String ENTITIES = "ENTITIES";
        const String NMTOKEN = "NMTOKEN";
        const String NMTOKENS = "NMTOKENS";
        const String REQUIRED = "#REQUIRED";
        const String IMPLIED = "#IMPLIED";
        const String FIXED = "#FIXED";
        const String PUBLIC = "PUBLIC";
        const String SYSTEM = "SYSTEM";
        const String INCLUDE = "INCLUDE";
        const String IGNORE = "IGNORE";

        #endregion

        #region Members

        Boolean _external;
        Char _endChar;
        IntermediateStream _stream;
        DtdContainer _container;
        Int32 _includes;

        #endregion

        #region ctor

        /// <summary>
        /// Creates a new DTD tokenizer with the given source and container.
        /// </summary>
        /// <param name="container">The container to use.</param>
        /// <param name="src">The source to inspect.</param>
        public DtdTokenizer(DtdContainer container, SourceManager src)
            : base(src)
        {
            _stream = new IntermediateStream(src);
            _container = container;
            _includes = 0;
            IsExternal = true;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the parsed content.
        /// </summary>
        public String Content
        {
            get { return _stream.Content; }
        }

        /// <summary>
        /// Gets or sets if the DTD is from an external source.
        /// </summary>
        public Boolean IsExternal 
        {
            get { return _external; }
            set
            {
                _external = value;
                _endChar = _external ? Specification.EOF : Specification.SBC;
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Scans the DTD in the doctype as specified in the
        /// official XML spec and (in German) here:
        /// http://www.stefanheymann.de/xml/dtdxml.htm
        /// </summary>
        public DtdToken Get()
        {
            var c = _stream.Current;
            var element = GetElement(c);

            if (element != DtdToken.EOF)
                SkipSpaces(c);

            return element;
        }

        #endregion

        #region General

        /// <summary>
        /// Gets the next found DTD element by advancing
        /// and applying the rules for DTD.
        /// </summary>
        /// <param name="c">The current character.</param>
        /// <returns>The found declaration.</returns>
        DtdToken GetElement(Char c)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == _endChar && _includes == 0)
                return DtdToken.EOF;
            
            if (c == Specification.SBC)
            {
                if (_includes > 0 && _stream.Next == Specification.SBC && _stream.Next == Specification.GT)
                {
                    _includes--;
                    return GetElement(_stream.Next);
                }
            }
            else if (c == Specification.LT)
            {
                c = _stream.Next;

                if (c == Specification.QM)
                {
                    return ProcessingStart(_stream.Next);
                }
                else if (c == Specification.EM)
                {
                    _stream.Advance();

                    if (_stream.ContinuesWith(ENTITY))
                    {
                        _stream.Advance(5);
                        c = _stream.Next;

                        if (c.IsSpaceCharacter())
                            return EntityDeclaration(c);
                    }
                    else if (_stream.ContinuesWith(ELEMENT))
                    {
                        _stream.Advance(6);
                        c = _stream.Next;

                        if (c.IsSpaceCharacter())
                            return TypeDeclaration(c);
                    }
                    else if (_stream.ContinuesWith(ATTLIST))
                    {
                        _stream.Advance(6);
                        c = _stream.Next;

                        if (c.IsSpaceCharacter())
                            return AttributeDeclaration(c);
                    }
                    else if (_stream.ContinuesWith(NOTATION))
                    {
                        _stream.Advance(7);
                        c = _stream.Next;

                        if (c.IsSpaceCharacter())
                            return NotationDeclaration(c);
                    }
                    else if (_stream.ContinuesWith("--"))
                    {
                        _stream.Advance();
                        return CommentStart(_stream.Next);
                    }
                    else if (_stream.Current == Specification.SBO && _external)
                        return Conditional(_stream.Next);
                }
            }
            else if (c == Specification.PERCENT)
            {
                PEReference(_stream.Next);
                return GetElement(_stream.Current);
            }

            throw Errors.GetException(ErrorCode.DtdInvalid);
        }

        #endregion

        #region Conditional

        /// <summary>
        /// Treats the conditional sects with respect.
        /// http://www.w3.org/TR/REC-xml/#sec-condition-sect
        /// </summary>
        /// <param name="c">The current character.</param>
        /// <returns>The evaluated token.</returns>
        DtdToken Conditional(Char c)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (_stream.ContinuesWith(INCLUDE))
            {
                _stream.Advance(6);

                do c = _stream.Next;
                while (c.IsSpaceCharacter());

                if (c == Specification.SBO)
                {
                    _includes++;
                    return GetElement(_stream.Next);
                }
            }
            else if (_stream.ContinuesWith(IGNORE))
            {
                _stream.Advance(5);

                do c = _stream.Next;
                while (c.IsSpaceCharacter());

                if (c == Specification.SBO)
                {
                    var nesting = 0;
                    var lastThree = new[] { Specification.NULL, Specification.NULL, Specification.NULL };

                    do
                    {
                        c = _stream.Next;

                        if (c == Specification.EOF)
                            break;

                        lastThree[0] = lastThree[1];
                        lastThree[1] = lastThree[2];
                        lastThree[2] = c;

                        if (lastThree[0] == Specification.LT && lastThree[1] == Specification.EM && lastThree[2] == Specification.SBO)
                            nesting++;
                    }
                    while (nesting == 0 && lastThree[0] == Specification.SBC && lastThree[1] == Specification.SBC && lastThree[2] == Specification.GT);

                    if (c == Specification.GT)
                        return GetElement(_stream.Next);
                }
            }

            throw Errors.GetException(ErrorCode.DtdInvalid);
        }

        #endregion

        #region Processing Instruction

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-pi.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken ProcessingStart(Char c)
        {
            if (c.IsXmlNameStart())
            {
                _stringBuffer.Clear();
                _stringBuffer.Append(c);
                return ProcessingTarget(_stream.Next, new DtdPIToken());
            }

            throw Errors.GetException(ErrorCode.XmlInvalidPI);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-pi.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="pi">The processing instruction token.</param>
        DtdToken ProcessingTarget(Char c, DtdPIToken pi)
        {
            while (c.IsXmlName())
            {
                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            pi.Target = _stringBuffer.ToString();
            _stringBuffer.Clear();

            if (String.Compare(pi.Target, Tags.XML, StringComparison.OrdinalIgnoreCase) == 0)
            {
                if (IsExternal)
                    return ProcessTextDecl(c);
            }
            else if (c == Specification.QM)
            {
                c = _stream.Next;

                if(c == Specification.GT)
                    return pi;
            }
            else if (c.IsSpaceCharacter())
                return ProcessingContent(_stream.Next, pi);

            throw Errors.GetException(ErrorCode.XmlInvalidPI);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-pi.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="pi">The processing instruction token.</param>
        DtdToken ProcessingContent(Char c, DtdPIToken pi)
        {
            while (true)
            {
                if (c == Specification.EOF)
                    throw Errors.GetException(ErrorCode.EOF);

                if (c == Specification.QM)
                {
                    c = _src.Next;

                    if (c == Specification.GT)
                    {
                        pi.Content = _stringBuffer.ToString();
                        return pi;
                    }

                    _stringBuffer.Append(Specification.QM);
                }
                else
                {
                    _stringBuffer.Append(c);
                    c = _src.Next;
                }
            }
        }

        DtdToken ProcessTextDecl(Char c)
        {
            var token = new DtdDeclToken();

            if (c.IsSpaceCharacter())
            {
                c = SkipSpaces(c);

                if (_stream.ContinuesWith(AttributeNames.VERSION))
                {
                    _stream.Advance(6);
                    return ProcessTextVersion(_stream.Next, token);
                }
                else if (_stream.ContinuesWith(AttributeNames.ENCODING))
                {
                    _stream.Advance(7);
                    return ProcessTextEncoding(_stream.Next, token);
                }
            }

            if (c == Specification.QM)
                return ProcessTextAfter(_stream.Next, token);

            throw Errors.GetException(ErrorCode.XmlInvalidPI);
        }

        DtdToken ProcessTextVersion(Char c, DtdDeclToken decl)
        {
            if (c == Specification.EQ)
            {
                var q = _stream.Next;

                if (q == Specification.DQ || q == Specification.SQ)
                {
                    _stringBuffer.Clear();
                    c = _stream.Next;

                    while (c.IsDigit() || c == Specification.DOT)
                    {
                        _stringBuffer.Append(c);
                        c = _stream.Next;
                    }

                    if (c == q)
                    {
                        decl.Version = _stringBuffer.ToString();
                        return ProcessTextBetween(_stream.Next, decl);
                    }
                }
            }

            throw Errors.GetException(ErrorCode.XmlInvalidPI);
        }

        DtdToken ProcessTextBetween(Char c, DtdDeclToken decl)
        {
            if (c.IsSpaceCharacter())
            {
                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (_stream.ContinuesWith(AttributeNames.ENCODING))
                {
                    _stream.Advance(7);
                    return ProcessTextEncoding(_stream.Next, decl);
                }
                else if (c == Specification.QM)
                    return ProcessTextAfter(_stream.Next, decl);
            }

            throw Errors.GetException(ErrorCode.XmlInvalidPI);
        }

        DtdToken ProcessTextEncoding(Char c, DtdDeclToken decl)
        {
            if (c == Specification.EQ)
            {
                var q = _stream.Next;

                if (q == Specification.DQ || q == Specification.SQ)
                {
                    _stringBuffer.Clear();
                    c = _stream.Next;

                    if (c.IsLetter())
                    {
                        do
                        {
                            _stringBuffer.Append(c);
                            c = _stream.Next;
                        }
                        while (c.IsAlphanumericAscii() || c == Specification.DOT || c == Specification.UNDERSCORE || c == Specification.MINUS);
                    }

                    if (c == q)
                    {
                        decl.Encoding = _stringBuffer.ToString();
                        return ProcessTextEnd(_stream.Next, decl);
                    }
                }
            }

            throw Errors.GetException(ErrorCode.XmlInvalidPI);
        }

        DtdToken ProcessTextEnd(Char c, DtdDeclToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.QM)
                return ProcessTextAfter(_stream.Next, decl);

            throw Errors.GetException(ErrorCode.XmlInvalidPI);
        }

        DtdToken ProcessTextAfter(Char c, DtdDeclToken decl)
        {
            if(c == Specification.GT)
                return decl;

            throw Errors.GetException(ErrorCode.XmlInvalidPI);
        }

        #endregion

        #region Comments

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken CommentStart(Char c)
        {
            _stringBuffer.Clear();
            return Comment(c);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken Comment(Char c)
        {
            while (c.IsXmlChar())
            {
                if (c == Specification.MINUS)
                    return CommentDash(_stream.Next);

                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            throw Errors.GetException(ErrorCode.CommentEndedUnexpected);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken CommentDash(Char c)
        {
            if (c == Specification.MINUS)
                return CommentEnd(_stream.Next);

            return Comment(c);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken CommentEnd(Char c)
        {
            if (c == Specification.GT)
                return new DtdCommentToken() { Data = _stringBuffer.ToString() };

            throw Errors.GetException(ErrorCode.CommentEndedUnexpected);
        }

        #endregion

        #region Declaration Name

        Boolean DeclarationNameBefore(Char c, DtdToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.EOF)
                throw Errors.GetException(ErrorCode.EOF);

            if (c == Specification.PERCENT)
            {
                PEReference(_stream.Next);
                return DeclarationNameBefore(_stream.Current, decl);
            }
            
            if (c.IsXmlNameStart())
            {
                _stringBuffer.Clear();
                _stringBuffer.Append(c);
                return DeclarationName(_stream.Next, decl);
            }

            return false;
        }

        Boolean DeclarationName(Char c, DtdToken decl)
        {
            while (c.IsXmlName())
            {
                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            if (c == Specification.PERCENT)
            {
                PEReference(_stream.Next);
                return DeclarationName(_stream.Current, decl);
            }

            decl.Name = _stringBuffer.ToString();
            _stringBuffer.Clear();

            if(c == Specification.EOF)
                throw Errors.GetException(ErrorCode.EOF);

            return c.IsSpaceCharacter();
        }

        #endregion

        #region References

        void PEReference(Char c, Boolean use = true)
        {
            var buffer = Pool.NewStringBuilder();

            if (c.IsXmlNameStart())
            {
                do
                {
                    buffer.Append(c);
                    c = _stream.Next;
                }
                while (c.IsXmlName());

                var temp = buffer.ToPool();

                if (c == Specification.SC)
                {
                    var p = _container.GetParameter(temp);

                    if (p != null)
                    {
                        if (use)
                        {
                            _stream.Push(temp.Length + 2, p.NodeValue);
                            return;
                        }
                        else
                            throw Errors.GetException(ErrorCode.DtdPEReferenceInvalid);
                    }
                }
            }

            if (use)
                throw Errors.GetException(ErrorCode.DtdPEReferenceInvalid);
            
            _stringBuffer.Append(Specification.PERCENT).Append(buffer.ToString());
        }

        void EReference(Char c)
        {
            var buffer = Pool.NewStringBuilder();

            if (c.IsXmlNameStart())
            {
                do
                {
                    buffer.Append(c);
                    c = _stream.Next;
                }
                while (c.IsXmlName());

                var temp = buffer.ToPool();

                if (temp.Length > 0 && c == Specification.SC)
                {
                    var p = _container.GetEntity(temp);

                    if (p != null)
                    {
                        _stream.Push(temp.Length + 2, p.NodeValue);
                        return;
                    }
                }
            }
            else if (c == Specification.NUM)
            {
                c = _src.Next;
                var hex = c == 'x' || c == 'X';

                if (hex)
                {
                    do
                    {
                        buffer.Append(c);
                        c = _src.Next;
                    }
                    while (c.IsHex());
                }
                else
                {
                    while (c.IsDigit())
                    {
                        buffer.Append(c);
                        c = _src.Next;
                    }
                }

                var temp = buffer.ToPool();

                if (temp.Length > 0 && c == Specification.SC)
                {
                    var num = hex ? temp.FromHex() : temp.FromDec();

                    if (num.IsValidAsCharRef())
                    {
                        var o = hex ? 4 : 3;
                        var p = Char.ConvertFromUtf32(num);
                        _stream.Push(temp.Length + o, p);
                        return;
                    }
                }
            }

            throw Errors.GetException(ErrorCode.CharacterReferenceNotTerminated);
        }

        #endregion

        #region Entity Declaration

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-entity-decl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken EntityDeclaration(Char c)
        {
            var decl = new DtdEntityToken();

            if (c.IsSpaceCharacter())
                c = SkipSpaces(c);

            if (c == Specification.PERCENT)
            {
                decl.IsParameter = true;

                if (!_stream.Next.IsSpaceCharacter())
                    throw Errors.GetException(ErrorCode.CharacterReferenceInvalidCode);

                c = SkipSpaces(c);
            }

            if (DeclarationNameBefore(c, decl))
            {
                c = SkipSpaces(c);

                if (_stream.ContinuesWith(SYSTEM))
                {
                    decl.IsExtern = true;
                    _stream.Advance(5);
                    c = _stream.Next;
                }
                else if (_stream.ContinuesWith(PUBLIC))
                {
                    decl.IsExtern = true;
                    _stream.Advance(5);
                    return EntityDeclarationBeforeSystem(_stream.Next, decl);
                }
                else
                    c = _stream.Previous;

                return EntityDeclarationBeforeValue(c, decl);
            }

            throw Errors.GetException(ErrorCode.DtdNameInvalid);
        }

        DtdToken EntityDeclarationBeforeValue(Char c, DtdEntityToken decl)
        {
            if (c.IsSpaceCharacter())
            {
                c = SkipSpaces(c);
                _stringBuffer.Clear();

                if (Specification.DQ == c || Specification.SQ == c)
                    return EntityDeclarationValue(_stream.Next, c, decl);
            }

            throw Errors.GetException(ErrorCode.DtdDeclInvalid);
        }

        DtdToken EntityDeclarationValue(Char c, Char end, DtdEntityToken decl)
        {
            while (c != end)
            {
                if (c == Specification.EOF)
                    throw Errors.GetException(ErrorCode.EOF);
                else if (c == Specification.PERCENT)
                {
                    PEReference(_stream.Next, IsExternal);
                    c = _stream.Current;
                    continue;
                }
                else if (c == Specification.AMPERSAND)
                {
                    EReference(_stream.Next);
                    c = _stream.Current;
                    continue;
                }

                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            decl.Value = _stringBuffer.ToString();
            return EntityDeclarationAfter(_stream.Next, decl);
        }

        DtdToken EntityDeclarationBeforeSystem(Char c, DtdEntityToken decl)
        {
            if (c.IsSpaceCharacter())
            {
                c = SkipSpaces(c);
                _stringBuffer.Clear();

                if (Specification.DQ == c || Specification.SQ == c)
                    return EntityDeclarationSystem(_stream.Next, c, decl);
            }

            throw Errors.GetException(ErrorCode.DtdDeclInvalid);
        }

        DtdToken EntityDeclarationSystem(Char c, Char quote, DtdEntityToken decl)
        {
            while (c != quote)
            {
                if (c == Specification.EOF)
                {
                    RaiseErrorOccurred(ErrorCode.EOF);
                    _stream.Back();
                    decl.PublicIdentifier = _stringBuffer.ToString();
                    return decl;
                }
                else if (c == Specification.NULL)
                {
                    RaiseErrorOccurred(ErrorCode.NULL);
                    _stringBuffer.Append(Specification.REPLACEMENT);
                }
                else if (c.IsPubidChar())
                    _stringBuffer.Append(c);
                else
                    RaiseErrorOccurred(ErrorCode.InvalidCharacter);

                c = _stream.Next;
            }

            decl.PublicIdentifier = _stringBuffer.ToString();
            return EntityDeclarationBeforeValue(_stream.Next, decl);
        }

        DtdToken EntityDeclarationAfter(Char c, DtdEntityToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.EOF)
                throw Errors.GetException(ErrorCode.EOF);
            else if (c == Specification.GT)
                return decl;
            else if (c.IsSpaceCharacter())
            {
                c = SkipSpaces(c);

                if (decl.IsExtern && !decl.IsParameter && String.IsNullOrEmpty(decl.ExternNotation) && _stream.ContinuesWith(NDATA))
                {
                    _stream.Advance(4);
                    c = _stream.Next;

                    while (c.IsSpaceCharacter())
                        c = _stream.Next;

                    if (c.IsXmlNameStart())
                    {
                        _stringBuffer.Clear();

                        do
                        {
                            _stringBuffer.Append(c);
                            c = _stream.Next;
                        }
                        while (c.IsXmlName());

                        decl.ExternNotation = _stringBuffer.ToString();
                        return EntityDeclarationAfter(c, decl);
                    }
                }
            }

            throw Errors.GetException(ErrorCode.DtdEntityInvalid);
        }

        #endregion

        #region Attribute Declaration

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#attdecls.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdAttributeToken AttributeDeclaration(Char c)
        {
            var decl = new DtdAttributeToken();

            if (DeclarationNameBefore(_stream.Next, decl))
            {
                c = SkipSpaces(c);

                while (true)
                {
                    if (c == Specification.GT)
                        return AttributeDeclarationAfter(c, decl);
                    else if (!c.IsXmlNameStart())
                        break;

                    _stringBuffer.Clear();
                    decl.Attributes.Add(AttributeDeclarationName(c));
                    c = _stream.Current;

                    if(c.IsSpaceCharacter())
                        c = SkipSpaces(c);
                }
            }

            throw Errors.GetException(ErrorCode.DtdAttListInvalid);
        }

        AttributeDeclarationEntry AttributeDeclarationName(Char c)
        {
            var value = new AttributeDeclarationEntry();

            do
            {
                _stringBuffer.Append(c);
                c = _stream.Next;
            }
            while (c.IsXmlName());

            if (!c.IsSpaceCharacter())
                throw Errors.GetException(ErrorCode.DtdAttListInvalid);

            value.Name = _stringBuffer.ToString();
            _stringBuffer.Clear();
            return AttributeDeclarationType(_stream.Next, value);
        }

        AttributeDeclarationEntry AttributeDeclarationType(Char c, AttributeDeclarationEntry value)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.RBO)
            {
                var type = new AttributeEnumeratedType();
                value.Type = type;
                AttributeDeclarationTypeEnumeration(_stream.Next, type);
            }
            else if (c.IsUppercaseAscii())
            {
                var id = String.Empty;

                while (true)
                {
                    if (c.IsSpaceCharacter())
                    {
                        id = _stringBuffer.ToString();
                        _stringBuffer.Clear();
                        break;
                    }
                    else if (c == Specification.GT)
                        throw Errors.GetException(ErrorCode.DtdDeclInvalid);
                    else if (c == Specification.NULL)
                    {
                        RaiseErrorOccurred(ErrorCode.NULL);
                        _stringBuffer.Append(Specification.REPLACEMENT);
                    }
                    else if (c == Specification.EOF)
                        throw Errors.GetException(ErrorCode.EOF);
                    else
                        _stringBuffer.Append(c);

                    c = _stream.Next;
                }

                switch (id)
                {
                    case CDATA:
                        value.Type = new AttributeStringType();
                        break;
                    case ID:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.ID };
                        break;
                    case IDREF:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.IDREF };
                        break;
                    case IDREFS:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.IDREFS };
                        break;
                    case ENTITY:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.ENTITY };
                        break;
                    case ENTITIES:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.ENTITIES };
                        break;
                    case NMTOKEN:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.NMTOKEN };
                        break;
                    case NMTOKENS:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.NMTOKENS };
                        break;
                    case NOTATION:
                        var type = new AttributeEnumeratedType { IsNotation = true };
                        value.Type = type;

                        while (c.IsSpaceCharacter())
                            c = _stream.Next;

                        if (c != Specification.RBO)
                            throw Errors.GetException(ErrorCode.DtdDeclInvalid);

                        AttributeDeclarationTypeEnumeration(_stream.Next, type);
                        break;
                    default:
                        throw Errors.GetException(ErrorCode.DtdDeclInvalid);
                }
            }

            return AttributeDeclarationValue(_stream.Next, value);
        }

        void AttributeDeclarationTypeEnumeration(Char c, AttributeEnumeratedType parent)
        {
            while (true)
            {
                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (c == Specification.EOF)
                    throw Errors.GetException(ErrorCode.EOF);

                if (!c.IsXmlName())
                    throw Errors.GetException(ErrorCode.DtdDeclInvalid);

                do
                {
                    _stringBuffer.Append(c);
                    c = _stream.Next;
                }
                while (c.IsXmlName());

                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                parent.Names.Add(_stringBuffer.ToString());
                _stringBuffer.Clear();

                if (c == Specification.RBC)
                    break;
                else if (c == Specification.PIPE)
                    c = _stream.Next;
                else
                    throw Errors.GetException(ErrorCode.DtdDeclInvalid);
            }
        }

        AttributeDeclarationEntry AttributeDeclarationValue(Char c, AttributeDeclarationEntry value)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            var isfixed = false;

            if (c == Specification.NUM)
            {
                do
                {
                    _stringBuffer.Append(c);
                    c = _stream.Next;

                    if (c == Specification.EOF)
                        throw Errors.GetException(ErrorCode.EOF);
                    else if (c == Specification.GT)
                        break;
                }
                while (!c.IsSpaceCharacter());

                var tag = _stringBuffer.ToString();
                _stringBuffer.Clear();

                switch (tag)
                {
                    case REQUIRED:
                        value.Default = new AttributeRequiredValue();
                        return value;
                    case IMPLIED:
                        value.Default = new AttributeImpliedValue();
                        return value;
                    case FIXED:
                        isfixed = true;
                        break;
                }

                while (c.IsSpaceCharacter())
                    c = _stream.Next;
            }

            var defvalue = AttributeDeclarationBeforeDefaultValue(c);
            _stringBuffer.Clear();
            _stream.Advance();

            value.Default = new AttributeCustomValue
            {
                Value = defvalue,
                IsFixed = isfixed
            };
            return value;
        }

        String AttributeDeclarationBeforeDefaultValue(Char c)
        {
            if (c == Specification.DQ || c == Specification.SQ)
                return AttributeDeclarationDefaultValue(_stream.Next, c);

            throw Errors.GetException(ErrorCode.DtdDeclInvalid);
        }

        String AttributeDeclarationDefaultValue(Char c, Char end)
        {
            while (c != end)
            {
                if (c == Specification.EOF)
                    throw Errors.GetException(ErrorCode.EOF);
                else if (c == Specification.LT)
                    throw Errors.GetException(ErrorCode.XmlLtInAttributeValue);
                else if (c == Specification.PERCENT)
                {
                    PEReference(_stream.Next, IsExternal);
                    c = _stream.Current;
                    continue;
                }
                else if (c == Specification.AMPERSAND)
                {
                    EReference(_stream.Next);
                    c = _stream.Current;
                    continue;
                }

                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            return _stringBuffer.ToString();
        }

        DtdAttributeToken AttributeDeclarationAfter(Char c, DtdAttributeToken decl)
        {
            var hasError = false;

            while (true)
            {
                if (c == Specification.GT)
                {
                    return decl;
                }
                else if (c == Specification.EOF)
                {
                    RaiseErrorOccurred(ErrorCode.EOF);
                    _stream.Back();
                    return decl;
                }
                else if (!c.IsSpaceCharacter())
                {
                    if (!hasError)
                        RaiseErrorOccurred(ErrorCode.InputUnexpected);

                    hasError = true;
                }

                c = _stream.Next;
            }
        }

        #endregion

        #region Notation Declaration

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#Notations.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdNotationToken NotationDeclaration(Char c)
        {
            var decl = new DtdNotationToken();
            var canContinue = false;

            if (c.IsSpaceCharacter())
                canContinue = DeclarationNameBefore(_stream.Next, decl);
            else if (c == Specification.EOF)
                throw Errors.GetException(ErrorCode.EOF);
            else
            {
                RaiseErrorOccurred(ErrorCode.UndefinedMarkupDeclaration);
                canContinue = DeclarationNameBefore(c, decl);
            }

            if (canContinue)
            {
                if (_stream.ContinuesWith(PUBLIC))
                {
                    _stream.Advance(5);
                    return NotationDeclarationBeforePublic(_stream.Next, decl);
                }
                else if (_stream.ContinuesWith(SYSTEM))
                {
                    _stream.Advance(5);
                    return NotationDeclarationBeforeSystem(_stream.Next, decl);
                }

                return NotationDeclarationAfterSystem(c, decl);
            }

            RaiseErrorOccurred(ErrorCode.NotationPublicInvalid);
            return decl;
        }

        DtdNotationToken NotationDeclarationBeforePublic(Char c, DtdNotationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.GT)
            {
                RaiseErrorOccurred(ErrorCode.TagClosedWrong);
                return decl;
            }
            else if (c == Specification.EOF)
            {
                RaiseErrorOccurred(ErrorCode.EOF);
                _stream.Back();
                return decl;
            }
            else if (c == Specification.SQ)
            {
                return NotationDeclarationPublic(_stream.Next, Specification.SQ, decl);
            }
            else if (c == Specification.DQ)
            {
                return NotationDeclarationPublic(_stream.Next, Specification.DQ, decl);
            }
            else
            {
                return NotationDeclarationAfterSystem(c, decl);
            }
        }

        DtdNotationToken NotationDeclarationPublic(Char c, Char quote, DtdNotationToken decl)
        {
            _stringBuffer.Clear();

            while (true)
            {
                if (c == Specification.EOF)
                {
                    RaiseErrorOccurred(ErrorCode.EOF);
                    _stream.Back();
                    decl.PublicIdentifier = _stringBuffer.ToString();
                    return decl;
                }
                else if (c == Specification.NULL)
                {
                    RaiseErrorOccurred(ErrorCode.NULL);
                    _stringBuffer.Append(Specification.REPLACEMENT);
                }
                else if (c == quote)
                {
                    decl.PublicIdentifier = _stringBuffer.ToString();
                    return NotationDeclarationAfterPublic(_stream.Next, decl);
                }
                else
                    _stringBuffer.Append(c);

                c = _stream.Next;
            }
        }

        DtdNotationToken NotationDeclarationAfterPublic(Char c, DtdNotationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.GT)
            {
                return decl;
            }
            else if (c == Specification.EOF)
            {
                RaiseErrorOccurred(ErrorCode.EOF);
                _stream.Back();
                return decl;
            }
            else if (c == Specification.SQ)
            {
                return NotationDeclarationSystem(_stream.Next, Specification.SQ, decl);
            }
            else if (c == Specification.DQ)
            {
                return NotationDeclarationSystem(_stream.Next, Specification.DQ, decl);
            }
            else
            {
                return NotationDeclarationAfterSystem(c, decl);
            }
        }

        DtdNotationToken NotationDeclarationBeforeSystem(Char c, DtdNotationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.GT)
            {
                RaiseErrorOccurred(ErrorCode.TagClosedWrong);
                return decl;
            }
            else if (c == Specification.EOF)
            {
                RaiseErrorOccurred(ErrorCode.EOF);
                _stream.Back();
                return decl;
            }
            else if (c == Specification.SQ)
            {
                return NotationDeclarationSystem(_stream.Next, Specification.SQ, decl);
            }
            else if (c == Specification.DQ)
            {
                return NotationDeclarationSystem(_stream.Next, Specification.DQ, decl);
            }
            else
            {
                RaiseErrorOccurred(ErrorCode.NotationSystemInvalid);
                return decl;
            }
        }

        DtdNotationToken NotationDeclarationSystem(Char c, Char quote, DtdNotationToken decl)
        {
            _stringBuffer.Clear();

            while (true)
            {
                if (c == Specification.EOF)
                {
                    RaiseErrorOccurred(ErrorCode.EOF);
                    _stream.Back();
                    decl.SystemIdentifier = _stringBuffer.ToString();
                    return decl;
                }
                else if (c == Specification.NULL)
                {
                    RaiseErrorOccurred(ErrorCode.NULL);
                    _stringBuffer.Append(Specification.REPLACEMENT);
                }
                else if (c == quote)
                {
                    decl.SystemIdentifier = _stringBuffer.ToString();
                    return NotationDeclarationAfterSystem(_stream.Next, decl);
                }
                else if (c.IsPubidChar())
                    _stringBuffer.Append(c);
                else
                    RaiseErrorOccurred(ErrorCode.InvalidCharacter);

                c = _stream.Next;
            }
        }

        DtdNotationToken NotationDeclarationAfterSystem(Char c, DtdNotationToken decl)
        {
            var hasError = false;

            while (true)
            {
                if (c == Specification.GT)
                {
                    return decl;
                }
                else if (c == Specification.EOF)
                {
                    RaiseErrorOccurred(ErrorCode.EOF);
                    _stream.Back();
                    return decl;
                }
                else if (!c.IsSpaceCharacter())
                {
                    if (!hasError)
                        RaiseErrorOccurred(ErrorCode.InputUnexpected);

                    hasError = true;
                }

                c = _stream.Next;
            }
        }

        #endregion

        #region Type Declaration

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#elemdecls.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdElementToken TypeDeclaration(Char c)
        {
            var decl = new DtdElementToken();

            if (DeclarationNameBefore(c, decl))
            {
                c = SkipSpaces(c);

                if (c == Specification.RBO)
                    return TypeDeclarationBeforeContent(_stream.Next, decl);
                else if (_stream.ContinuesWith(ANY))
                {
                    _stream.Advance(2);
                    decl.Entry = ElementDeclarationEntry.Any;
                    return TypeDeclarationAfterContent(_stream.Next, decl);
                }
                else if (_stream.ContinuesWith(EMPTY))
                {
                    _stream.Advance(4);
                    decl.Entry = ElementDeclarationEntry.Empty;
                    return TypeDeclarationAfterContent(_stream.Next, decl);
                }

                return TypeDeclarationAfterContent(c, decl);
            }

            throw Errors.GetException(ErrorCode.DtdTypeInvalid);
        }

        DtdElementToken TypeDeclarationBeforeContent(Char c, DtdElementToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (_stream.ContinuesWith(PCDATA))
            {
                _stream.Advance(6);
                decl.Entry = TypeDeclarationMixed(_stream.Next);
            }
            else
            {
                decl.Entry = TypeDeclarationChildren(c);
            }

            return TypeDeclarationAfterContent(_stream.Current, decl);
        }

        ElementChildrenDeclarationEntry TypeDeclarationChildren(Char c)
        {
            var entries = new List<ElementQuantifiedDeclarationEntry>();
            var connection = Specification.NULL;

            while (true)
            {
                if (entries.Count > 0)
                {
                    if (c != Specification.PIPE && c != Specification.COMMA)
                        throw Errors.GetException(ErrorCode.DtdTypeContent);

                    if (entries.Count == 1)
                        connection = c;
                    else if (connection != c)
                        throw Errors.GetException(ErrorCode.DtdTypeContent);

                    c = _stream.Next;
                }

                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (c.IsXmlNameStart())
                {
                    var name = TypeDeclarationName(c);
                    entries.Add(name);
                }
                else if (c == Specification.RBO)
                    entries.Add(TypeDeclarationChildren(_stream.Next));
                else
                    throw Errors.GetException(ErrorCode.DtdTypeContent);

                c = _stream.Current;

                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (c == Specification.RBC)
                    break;
            }

            c = _stream.Next;

            if (entries.Count == 0)
                throw Errors.GetException(ErrorCode.DtdTypeContent);
            else if (connection == Specification.COMMA)
            {
                var sequence = new ElementSequenceDeclarationEntry();
                sequence.Sequence.AddRange(entries);
                sequence.Quantifier = TypeDeclarationQuantifier(c);
                return sequence;
            }

            var choice = new ElementChoiceDeclarationEntry();
            choice.Choice.AddRange(entries);
            choice.Quantifier = TypeDeclarationQuantifier(c);
            return choice;
        }

        ElementNameDeclarationEntry TypeDeclarationName(Char c)
        {
            _stringBuffer.Clear();
            _stringBuffer.Append(c);

            while ((c = _stream.Next).IsXmlName())
                _stringBuffer.Append(c);

            return new ElementNameDeclarationEntry
            {
                Name = _stringBuffer.ToString(),
                Quantifier = TypeDeclarationQuantifier(c)
            };
        }

        ElementQuantifier TypeDeclarationQuantifier(Char c)
        {
            switch (c)
            {
                case Specification.ASTERISK:
                    _stream.Advance();
                    return ElementQuantifier.ZeroOrMore;

                case Specification.QM:
                    _stream.Advance();
                    return ElementQuantifier.ZeroOrOne;

                case Specification.PLUS:
                    _stream.Advance();
                    return ElementQuantifier.OneOrMore;

                default:
                    return ElementQuantifier.One;
            }
        }

        ElementMixedDeclarationEntry TypeDeclarationMixed(Char c)
        {
            var entry = new ElementMixedDeclarationEntry();

            while (true)
            {
                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (c == Specification.RBC)
                {
                    c = _stream.Next;

                    if (c == Specification.ASTERISK)
                    {
                        entry.Quantifier = ElementQuantifier.ZeroOrMore;
                        _stream.Advance();
                        return entry;
                    }

                    if (entry.Names.Count > 0)
                        RaiseErrorOccurred(ErrorCode.QuantifierMissing);

                    break;
                }
                else if (c == Specification.PIPE)
                {
                    c = _stream.Next;

                    while (c.IsSpaceCharacter())
                        c = _stream.Next;

                    _stringBuffer.Clear();

                    if (c.IsXmlNameStart())
                    {
                        _stringBuffer.Append(c);

                        while ((c = _stream.Next).IsXmlName())
                            _stringBuffer.Append(c);

                        entry.Names.Add(_stringBuffer.ToString());
                    }
                    else
                        throw Errors.GetException(ErrorCode.DtdTypeContent);
                }
                else
                    throw Errors.GetException(ErrorCode.DtdTypeContent);
            }

            return entry;
        }

        DtdElementToken TypeDeclarationAfterContent(Char c, DtdElementToken decl)
        {
            while (true)
            {
                if (c == Specification.GT)
                    return decl;
                else if (c == Specification.EOF)
                    throw Errors.GetException(ErrorCode.EOF);
                else if (!c.IsSpaceCharacter())
                    throw Errors.GetException(ErrorCode.DtdTypeInvalid);

                c = _stream.Next;
            }
        }

        #endregion

        #region Intermediate Stream

        sealed class IntermediateStream
        {
            #region Members

            SourceManager _base;
            StringBuilder _buffer;
            Int32 _head;
            Int32 _start;
            Int32 _end;

            #endregion

            #region ctor

            public IntermediateStream(SourceManager src)
            {
                _head = 0;
                _start = src.InsertionPoint - 1;
                _buffer = new StringBuilder();
                _base = src;
            }

            #endregion

            #region Properties

            /// <summary>
            /// The content (of the original stream).
            /// </summary>
            public String Content
            {
                get { return _base.Copy(_start, _end); }
            }

            /// <summary>
            /// The previous character.
            /// </summary>
            public Char Previous
            {
                get
                {
                    Back();
                    return Current;
                }
            }

            /// <summary>
            /// The next character.
            /// </summary>
            public Char Next
            {
                get 
                {
                    if (_head == _buffer.Length)
                    {
                        _buffer.Append(_base.Current);
                        _end = _base.InsertionPoint;
                        _head++;
                        return _base.Next;
                    }
                    else if (_head == _buffer.Length - 1)
                    {
                        _head++;
                        return _base.Current;
                    }

                    return _buffer[++_head];
                }
            }

            /// <summary>
            /// The current character.
            /// </summary>
            public Char Current 
            {
                get { return _buffer.Length == _head ? _base.Current : _buffer[_head]; }
            }

            #endregion

            #region Methods

            /// <summary>
            /// Pushes the text at the current point and removes
            /// the given number of characters.
            /// </summary>
            /// <param name="remove">The number of characters to remove.</param>
            /// <param name="text">The text to insert.</param>
            public void Push(Int32 remove, String text)
            {
                Advance();
                var index = _head - remove;
                _buffer.Remove(index, remove);
                _buffer.Insert(index, text);
                _head = index;
            }

            /// <summary>
            /// Advances by one character.
            /// </summary>
            public void Advance()
            {
                if (_head == _buffer.Length)
                {
                    _buffer.Append(_base.Current);
                    _end = _base.InsertionPoint;
                    _base.Advance();
                }

                _head++;
            }

            /// <summary>
            /// Goes back by one character.
            /// </summary>
            public void Back()
            {
                _head--;
            }

            /// <summary>
            /// Advances by n characters.
            /// </summary>
            /// <param name="n">The number of characters to skip.</param>
            public void Advance(Int32 n)
            {
                for (int i = 0; i < n; i++)
                    Advance();
            }

            /// <summary>
            /// Checks if the stream continues with the given word.
            /// </summary>
            /// <param name="word">The word to check for.</param>
            /// <returns>True if it continues, otherwise false.</returns>
            public Boolean ContinuesWith(String word)
            {
                if (_head == _buffer.Length)
                    return _base.ContinuesWith(word, false);

                var current = _head;

                for (int i = 0; i < word.Length; i++)
                {
                    if (Current != word[i])
                    {
                        _head = current;
                        return false;
                    }

                    Advance();
                }

                _head = current;
                return true;
            }

            #endregion
        }

        #endregion

        #region Helper

        Char SkipSpaces(Char c)
        {
            do
            {
                c = _stream.Next;
            }
            while (c.IsSpaceCharacter());

            return c;
        }

        #endregion
    }
}
