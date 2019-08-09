using System.IO;
using Microsoft.CodeAnalysis;

namespace Semmle.Extraction.CSharp.Entities
{
    class PointerType : Type<IPointerTypeSymbol>
    {
        PointerType(Context cx, IPointerTypeSymbol init)
            : base(cx, init)
        {
            PointedAtType = Create(cx, symbol.PointedAtType);
        }

        public override void WriteId(TextWriter trapFile)
        {
            trapFile.WriteSubId(PointedAtType);
            trapFile.Write("*;type");
        }

        // All pointer types are extracted because they won't
        // be extracted in their defining assembly.
        public override bool NeedsPopulation => true;

        public override void Populate(TextWriter trapFile)
        {
            trapFile.Emit(Tuples.pointer_referent_type(this, PointedAtType.TypeRef));
            ExtractType(trapFile);
        }

        public Type PointedAtType { get; private set; }

        public static PointerType Create(Context cx, IPointerTypeSymbol symbol) => PointerTypeFactory.Instance.CreateEntity(cx, symbol);

        class PointerTypeFactory : ICachedEntityFactory<IPointerTypeSymbol, PointerType>
        {
            public static readonly PointerTypeFactory Instance = new PointerTypeFactory();

            public PointerType Create(Context cx, IPointerTypeSymbol init) => new PointerType(cx, init);
        }
    }
}
