using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Semmle.Extraction.CSharp.Populators;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Semmle.Extraction.CSharp.Entities
{
    public abstract class Method : CachedSymbol<IMethodSymbol>, IExpressionParentEntity, IStatementParentEntity
    {
        public Method(Context cx, IMethodSymbol init)
            : base(cx, init) { }

        protected void ExtractParameters(TextWriter trapFile)
        {
            var originalMethod = OriginalDefinition;
            IEnumerable<IParameterSymbol> parameters = symbol.Parameters;
            IEnumerable<IParameterSymbol> originalParameters = originalMethod.symbol.Parameters;

            if (IsReducedExtension)
            {
                if (this == originalMethod)
                {
                    // Non-generic reduced extensions must be extracted exactly like the
                    // non-reduced counterparts
                    parameters = symbol.ReducedFrom.Parameters;
                }
                else
                {
                    // Constructed reduced extensions are special because their non-reduced
                    // counterparts are not constructed. Therefore, we need to manually add
                    // the `this` parameter based on the type of the receiver
                    var originalThisParamSymbol = originalMethod.symbol.Parameters.First();
                    var originalThisParam = Parameter.Create(Context, originalThisParamSymbol, originalMethod);
                    ConstructedExtensionParameter.Create(Context, this, originalThisParam);
                    originalParameters = originalParameters.Skip(1);
                }
            }

            foreach (var p in parameters.Zip(originalParameters, (paramSymbol, originalParam) => new { paramSymbol, originalParam }))
            {
                var original = Equals(p.paramSymbol, p.originalParam) ? null : Parameter.Create(Context, p.originalParam, originalMethod);
                Parameter.Create(Context, p.paramSymbol, this, original);
            }

            if (symbol.IsVararg)
            {
                // Mono decided that "__arglist" should be an explicit parameter,
                // so now we need to populate it.

                VarargsParam.Create(Context, this);
            }
        }

        /// <summary>
        /// Extracts constructor initializers.
        /// </summary>
        protected virtual void ExtractInitializers(TextWriter trapFile)
        {
            // Normal methods don't have initializers,
            // so there's nothing to extract.
        }

        void ExtractMethodBody(TextWriter trapFile)
        {
            if (!IsSourceDeclaration)
                return;

            var block = Block;
            var expr = ExpressionBody;

            if (block != null || expr != null)
                Context.PopulateLater(
                    () =>
                    {
                        ExtractInitializers(trapFile);
                        if (block != null)
                            Statements.Block.Create(Context, block, this, 0);
                        else
                            Expression.Create(Context, expr, this, 0);

                        Context.NumberOfLines(trapFile, symbol, this);
                    });
        }

        public void Overrides(TextWriter trapFile)
        {
            foreach (var explicitInterface in symbol.ExplicitInterfaceImplementations.
                Where(sym => sym.MethodKind == MethodKind.Ordinary).
                Select(impl => Type.Create(Context, impl.ContainingType)))
            {
                Context.Emit(Tuples.explicitly_implements(this, explicitInterface.TypeRef));

                if (IsSourceDeclaration)
                    foreach (var syntax in symbol.DeclaringSyntaxReferences.Select(d => d.GetSyntax()).OfType<MethodDeclarationSyntax>())
                        TypeMention.Create(Context, syntax.ExplicitInterfaceSpecifier.Name, this, explicitInterface);
            }

            if (symbol.OverriddenMethod != null)
            {
                trapFile.Emit(Tuples.overrides(this, Method.Create(Context, symbol.OverriddenMethod)));
            }
        }

        /// <summary>
        ///  Factored out to share logic between `Method` and `UserOperator`.
        /// </summary>
        protected static void BuildMethodId(Method m, TextWriter tw)
        {
            tw.WriteSubId(m.ContainingType);

            AddExplicitInterfaceQualifierToId(m.Context, tw, m.symbol.ExplicitInterfaceImplementations);

            tw.Write(".");
            tw.Write(m.symbol.Name);

            if (m.symbol.IsGenericMethod)
            {
                if (Equals(m.symbol, m.symbol.OriginalDefinition))
                {
                    tw.Write('`');
                    tw.Write(m.symbol.TypeParameters.Length);
                }
                else
                {
                    tw.Write('<');
                    // Encode the nullability of the type arguments in the label.
                    // Type arguments with different nullability can result in 
                    // a constructed method with different nullability of its parameters and return type,
                    // so we need to create a distinct database entity for it.
                    tw.BuildList(",", m.symbol.GetAnnotatedTypeArguments(), (ta, tb0) => { AddSignatureTypeToId(m.Context, tb0, m.symbol, ta.Symbol); tw.Write((int)ta.Nullability); });
                    tw.Write('>');
                }
            }

            AddParametersToId(m.Context, tw, m.symbol);
            switch (m.symbol.MethodKind)
            {
                case MethodKind.PropertyGet:
                    tw.Write(";getter");
                    break;
                case MethodKind.PropertySet:
                    tw.Write(";setter");
                    break;
                case MethodKind.EventAdd:
                    tw.Write(";adder");
                    break;
                case MethodKind.EventRaise:
                    tw.Write(";raiser");
                    break;
                case MethodKind.EventRemove:
                    tw.Write(";remover");
                    break;
                default:
                    tw.Write(";method");
                    break;
            }
        }

        public override void WriteId(TextWriter trapFile)
        {
            BuildMethodId(this, trapFile);
        }

        /// <summary>
        /// Adds an appropriate label ID to the trap builder <paramref name="tb"/>
        /// for the type <paramref name="type"/> belonging to the signature of method
        /// <paramref name="method"/>.
        ///
        /// For methods without type parameters this will always add the key of the
        /// corresponding type.
        ///
        /// For methods with type parameters, this will add the key of the
        /// corresponding type if the type does *not* contain one of the method
        /// type parameters, otherwise it will add a textual representation of
        /// the type. This distinction is required because type parameter IDs
        /// refer to their declaring methods.
        ///
        /// Example:
        ///
        /// <code>
        /// int Count&lt;T&gt;(IEnumerable<T> items)
        /// </code>
        ///
        /// The label definitions for <code>Count</code> (<code>#4</code>) and <code>T</code>
        /// (<code>#5</code>) will look like:
        ///
        /// <code>
        /// #1=&lt;label for System.Int32&gt;
        /// #2=&lt;label for type containing Count&gt;
        /// #3=&lt;label for IEnumerable`1&gt;
        /// #4=@"{#1} {#2}.Count`2(#3<T>);method"
        /// #5=@"{#4}T;typeparameter"
        /// </code>
        ///
        /// Note how <code>int</code> is referenced in the label definition <code>#3</code> for
        /// <code>Count</code>, while <code>T[]</code> is represented textually in order
        /// to make the reference to <code>#3</code> in the label definition <code>#4</code> for
        /// <code>T</code> valid.
        /// </summary>
        protected static void AddSignatureTypeToId(Context cx, TextWriter tb, IMethodSymbol method, ITypeSymbol type)
        {
            if (type.ContainsTypeParameters(cx, method))
                type.BuildTypeId(cx, tb, (cx0, tb0, type0) => AddSignatureTypeToId(cx, tb0, method, type0));
            else
                tb.WriteSubId(Type.Create(cx, type));
        }

        protected static void AddParametersToId(Context cx, TextWriter tb, IMethodSymbol method)
        {
            tb.Write('(');
            int index = 0;

            if (method.MethodKind == MethodKind.ReducedExtension)
            {
                tb.WriteSeparator(",", index++);
                AddSignatureTypeToId(cx, tb, method, method.ReceiverType);
            }

            foreach (var param in method.Parameters)
            {
                tb.WriteSeparator(",", index++);
                AddSignatureTypeToId(cx, tb, method, param.Type);
                switch (param.RefKind)
                {
                    case RefKind.Out:
                        tb.Write(" out");
                        break;
                    case RefKind.Ref:
                        tb.Write(" ref");
                        break;
                }
            }

            if (method.IsVararg)
            {
                tb.WriteSeparator(",", index++);
                tb.Write("__arglist");
            }

            tb.Write(')');
        }

        public static void AddExplicitInterfaceQualifierToId(Context cx, System.IO.TextWriter tw, IEnumerable<ISymbol> explicitInterfaceImplementations)
        {
            if (explicitInterfaceImplementations.Any())
            {
                tw.AppendList(",", explicitInterfaceImplementations.Select(impl => cx.CreateEntity(impl.ContainingType)));
            }
        }

        public virtual string Name => symbol.Name;

        /// <summary>
        /// Creates a method of the appropriate subtype.
        /// </summary>
        /// <param name="cx"></param>
        /// <param name="methodDecl"></param>
        /// <returns></returns>
        public static Method Create(Context cx, IMethodSymbol methodDecl)
        {
            if (methodDecl == null) return null;

            var methodKind = methodDecl.MethodKind;

            if(methodKind == MethodKind.ExplicitInterfaceImplementation)
            {
                // Retrieve the original method kind
                methodKind = methodDecl.ExplicitInterfaceImplementations.Select(m => m.MethodKind).FirstOrDefault();
            }

            switch (methodKind)
            {
                case MethodKind.StaticConstructor:
                case MethodKind.Constructor:
                    return Constructor.Create(cx, methodDecl);
                case MethodKind.ReducedExtension:
                case MethodKind.Ordinary:
                case MethodKind.DelegateInvoke:
                    return OrdinaryMethod.Create(cx, methodDecl);
                case MethodKind.Destructor:
                    return Destructor.Create(cx, methodDecl);
                case MethodKind.PropertyGet:
                case MethodKind.PropertySet:
                    return methodDecl.AssociatedSymbol is null ? OrdinaryMethod.Create(cx, methodDecl) : (Method)Accessor.Create(cx, methodDecl);
                case MethodKind.EventAdd:
                case MethodKind.EventRemove:
                    return EventAccessor.Create(cx, methodDecl);
                case MethodKind.UserDefinedOperator:
                case MethodKind.BuiltinOperator:
                    return UserOperator.Create(cx, methodDecl);
                case MethodKind.Conversion:
                    return Conversion.Create(cx, methodDecl);
                case MethodKind.AnonymousFunction:
                    throw new InternalError(methodDecl, "Attempt to create a lambda");
                case MethodKind.LocalFunction:
                    return LocalFunction.Create(cx, methodDecl);
                default:
                    throw new InternalError(methodDecl, $"Unhandled method '{methodDecl}' of kind '{methodDecl.MethodKind}'");
            }
        }

        public Method OriginalDefinition =>
            IsReducedExtension
                ? Create(Context, symbol.ReducedFrom)
                : Create(Context, symbol.OriginalDefinition);

        public override Microsoft.CodeAnalysis.Location FullLocation => ReportingLocation;

        public override bool IsSourceDeclaration => symbol.IsSourceDeclaration();

        /// <summary>
        /// Whether this method has type parameters.
        /// </summary>
        public bool IsGeneric => symbol.IsGenericMethod;

        /// <summary>
        /// Whether this method has unbound type parameters.
        /// </summary>
        public bool IsUnboundGeneric => IsGeneric && Equals(symbol.ConstructedFrom, symbol);

        public bool IsBoundGeneric => IsGeneric && !IsUnboundGeneric;

        bool IsReducedExtension => symbol.MethodKind == MethodKind.ReducedExtension;

        protected IMethodSymbol ConstructedFromSymbol => symbol.ConstructedFrom.ReducedFrom ?? symbol.ConstructedFrom;

        bool IExpressionParentEntity.IsTopLevelParent => true;

        bool IStatementParentEntity.IsTopLevelParent => true;

        protected void ExtractGenerics(TextWriter trapFile)
        {
            var isFullyConstructed = IsBoundGeneric;

            if (IsGeneric)
            {
                int child = 0;

                if (isFullyConstructed)
                {
                    trapFile.Emit(Tuples.is_constructed(this));
                    trapFile.Emit(Tuples.constructed_generic(this, Method.Create(Context, ConstructedFromSymbol)));
                    foreach (var tp in symbol.GetAnnotatedTypeArguments())
                    {
                        trapFile.Emit(Tuples.type_arguments(Type.Create(Context, tp.Symbol), child, this));
                        var ta = tp.Nullability.GetTypeAnnotation();
                        if (ta != Kinds.TypeAnnotation.None)
                            trapFile.Emit(Tuples.type_argument_annotation(this, child, ta));
                        child++;
                    }
                }
                else
                {
                    trapFile.Emit(Tuples.is_generic(this));
                    foreach (var typeParam in symbol.TypeParameters.Select(tp => TypeParameter.Create(Context, tp)))
                    {
                        trapFile.Emit(Tuples.type_parameters(typeParam, child, this));
                        child++;
                    }
                }
            }
        }

        protected void ExtractRefReturn(TextWriter trapFile)
        {
            if (symbol.ReturnsByRef)
                trapFile.Emit(Tuples.type_annotation(this, Kinds.TypeAnnotation.Ref));
            if (symbol.ReturnsByRefReadonly)
                trapFile.Emit(Tuples.type_annotation(this, Kinds.TypeAnnotation.ReadonlyRef));
        }

        protected void PopulateMethod(TextWriter trapFile)
        {
            // Common population code for all callables
            BindComments();
            ExtractAttributes();
            ExtractParameters(trapFile);
            ExtractMethodBody(trapFile);
            ExtractGenerics(trapFile);
            ExtractMetadataHandle(trapFile);
            ExtractNullability(trapFile, symbol.ReturnNullableAnnotation);
        }

        public override TrapStackBehaviour TrapStackBehaviour => TrapStackBehaviour.PushesLabel;
    }
}
