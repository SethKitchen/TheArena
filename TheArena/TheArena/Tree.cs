using System;
using System.Collections.Generic;
using System.Text;

namespace TheArena
{
    public class GenericTree<T> where T : GenericTree<T> // recursive constraint  
    {
        // no specific data declaration  

        protected List<T> children;

        public GenericTree()
        {
            this.children = new List<T>();
        }

        public virtual void AddChild(T newChild)
        {
            this.children.Add(newChild);
        }

        public void Traverse(Action<int, T> visitor)
        {
            this.Traverse(0, visitor);
        }

        protected virtual void Traverse(int depth, Action<int, T> visitor)
        {
            visitor(depth, (T)this);
            foreach (T child in this.children)
                child.Traverse(depth + 1, visitor);
        }
    }
}
