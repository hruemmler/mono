//
// XslVariable.cs
//
// Authors:
//	Ben Maurer (bmaurer@users.sourceforge.net)
//	Atsushi Enomoto (ginga@kit.hi-ho.ne.jp)
//	
// (C) 2003 Ben Maurer
// (C) 2003 Atsushi Enomoto
//

using System;
using System.Collections;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;
using Mono.Xml.XPath;

using QName = System.Xml.XmlQualifiedName;

namespace Mono.Xml.Xsl.Operations {
	
	public class XslVariableInformation
	{
		QName name;
		XPathExpression select;
		XslOperation content;
		
		public XslVariableInformation (Compiler c)
		{
			c.AssertAttribute ("name");
			c.Input.MoveToFirstAttribute ();
			do {
				if (c.Input.NamespaceURI != String.Empty)
					continue;
				switch (c.Input.LocalName) {
				case "name":
				case "select":
					break;
				default:
					throw new XsltCompileException ("Invalid attribute " + c.Input.Name, null, c.Input);
				}
			} while (c.Input.MoveToNextAttribute ());
			c.Input.MoveToParent ();

			name = c.ParseQNameAttribute ("name");
			try {
				XmlConvert.VerifyName (name.Name);
			} catch (XmlException ex) {
				throw new XsltCompileException ("Variable name is not qualified name.", ex, c.Input);
			}

			string sel = c.GetAttribute ("select");
			if (sel != null && sel != "" ) {
				select = c.CompileExpression (c.GetAttribute ("select"));
				// TODO assert empty
			} else  if (c.Input.MoveToFirstChild ()) {
				content = c.CompileTemplateContent ();
				c.Input.MoveToParent ();
			}
		}
		
		public object Evaluate (XslTransformProcessor p)
		{
			if (select != null) {
				return p.Evaluate (select);
			} else if (content != null) {
//				XmlNodeWriter w = new XmlNodeWriter (false);
				DTMXPathDocumentWriter w = new DTMXPathDocumentWriter (p.CurrentNode.NameTable, 200);
				Outputter outputter = new GenericOutputter(w, p.Outputs, null, true);
				p.PushOutput (outputter);
				content.Evaluate (p);
				p.PopOutput ();
//				return w.Document.CreateNavigator ().SelectChildren (XPathNodeType.All);
				return w.CreateDocument ().CreateNavigator ().SelectChildren (XPathNodeType.All);
			} else {
				return "";
			}
		}
		
		public QName Name { get { return name; }}
	}
	
	public abstract class XslGeneralVariable : XslCompiledElement, IXsltContextVariable {
		protected XslVariableInformation var;	
		
		public XslGeneralVariable (Compiler c) : base (c) {}
		
		protected override void Compile (Compiler c)
		{
			this.var = new XslVariableInformation (c);
		}
		
		public override abstract void Evaluate (XslTransformProcessor p);
		protected abstract object GetValue (XslTransformProcessor p);
		
		
		public object Evaluate (XsltContext xsltContext)
		{	
			object value = GetValue (((XsltCompiledContext)xsltContext).Processor);
			
			if (value is XPathNodeIterator)			
				return ((XPathNodeIterator)value).Clone ();
			
			return value;
		}

		public QName Name {get {return  var.Name;}}
		public XPathResultType VariableType { get {return XPathResultType.Any;}}
		public abstract bool IsLocal { get; }
		public abstract bool IsParam { get; }
	}
	
	public class XslGlobalVariable : XslGeneralVariable {
		public XslGlobalVariable (Compiler c) : base (c) {}
		static object busyObject = new Object ();
		
			
		public override void Evaluate (XslTransformProcessor p)
		{
			Hashtable varInfo = p.globalVariableTable;
			
			if (varInfo.Contains (this)) {
				if (varInfo [this] == busyObject)
					throw new XsltException ("Circular dependency was detected.", null, p.CurrentNode);
				return;
			}
			
			varInfo [this] = busyObject;
			varInfo [this] = var.Evaluate (p);
			
		}
		
		protected override object GetValue (XslTransformProcessor p)
		{
			Evaluate (p);
			return p.globalVariableTable [this];
		}
			
		public override bool IsLocal { get { return false; }}
		public override bool IsParam { get { return false; }}
	}
	
	public class XslGlobalParam : XslGlobalVariable {
		bool overriden;
		object paramVal;
		
		public XslGlobalParam (Compiler c) : base (c) {}
			
		public void Override (XslTransformProcessor p, object paramVal)
		{
			Debug.Assert (!p.globalVariableTable.Contains (this), "Shouldn't have been evaluated by this point");
			
			p.globalVariableTable [this] = paramVal;
		}
		
		public override bool IsParam { get { return true; }}
	}
	
	public class XslLocalVariable : XslGeneralVariable {
		protected int slot;
				
		public XslLocalVariable (Compiler c) : base (c)
		{
			slot = c.AddVariable (this);
		}
		
		public override void Evaluate (XslTransformProcessor p)
		{	
			p.SetStackItem (slot, var.Evaluate (p));
		}
		
		protected override object GetValue (XslTransformProcessor p)
		{
			return p.GetStackItem (slot);
		}
		
		public bool IsEvaluated (XslTransformProcessor p)
		{
			return p.GetStackItem (slot) != null;
		}
		
		public override bool IsLocal { get { return true; }}
		public override bool IsParam { get { return false; }}
	}
	
	public class XslLocalParam : XslLocalVariable {
		bool overriden;
		object paramVal;
		
		public XslLocalParam (Compiler c) : base (c) {}
		
		public override void Evaluate (XslTransformProcessor p)
		{		
			if (p.GetStackItem (slot) != null)
				return; // evaluated already
				
			base.Evaluate (p);
		}
		
		public void Override (XslTransformProcessor p, object paramVal)
		{
			p.SetStackItem (slot, paramVal);
		}
		
		public override bool IsParam { get { return true; }}
	}
	
	internal class XPathVariableBinding : Expression {
		XslGeneralVariable v;
		public XPathVariableBinding (XslGeneralVariable v)
		{
			this.v = v;
		}
		public override String ToString () { return "$" + v.Name.ToString (); }
		public override XPathResultType ReturnType { get { return XPathResultType.Any; }}
		public override XPathResultType GetReturnType (BaseIterator iter)
		{
			return XPathResultType.Any;
		}
		
		public override object Evaluate (BaseIterator iter)
		{
			return v.Evaluate (iter.NamespaceManager as XsltContext);
		}
	}
}
