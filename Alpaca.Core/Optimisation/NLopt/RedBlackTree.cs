// Ported from NLopt, src/util/redblack.c: a simple implementation of red-black trees optimized
// for use with DIRECT.
//
// Copyright (c) 2007-2014 Massachusetts Institute of Technology
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;

namespace Alpaca4d.Optimisation.NLopt
{
    /// <summary>
    /// NLopt's red-black tree, node for node. Equal keys go left on insertion and removal swaps in
    /// the largest key on the left, as in the C, so DIRECT and CRS walk their trees in the same
    /// order as NLopt does, ties included; a SortedSet would not.
    ///
    /// The C keys are double pointers into the algorithms' own arrays; here a key is any object,
    /// and a comparison that broke ties by pointer breaks them by whatever the key carries for it.
    /// </summary>
    internal sealed class RedBlackTree<TKey> where TKey : class
    {
        public enum Color { Red, Black }

        public sealed class Node
        {
            internal Node P, R, L; // parent, right, left
            public TKey K;         // key (and data)
            internal Color C;
        }

        // It is convenient to use an explicit node for NULL nodes; it is never changed through one
        // of the pointers. The C shares one between all trees, one per tree does the same.
        private readonly Node _nil;
        private readonly Comparison<TKey> _compare;
        private Node _root;

        /// <summary>The number of nodes.</summary>
        public int N { get; private set; }

        public RedBlackTree(Comparison<TKey> compare)
        {
            _nil = new Node { C = Color.Black };
            _nil.P = _nil.R = _nil.L = _nil;
            _compare = compare;
            _root = _nil;
            N = 0;
        }

        private void RotateLeft(Node p)
        {
            Node n = p.R; // must be non-NIL
            p.R = n.L;
            n.L = p;
            if (p.P != _nil)
            {
                if (p == p.P.L) p.P.L = n;
                else p.P.R = n;
            }
            else
                _root = n;
            n.P = p.P;
            p.P = n;
            if (p.R != _nil) p.R.P = p;
        }

        private void RotateRight(Node p)
        {
            Node n = p.L; // must be non-NIL
            p.L = n.R;
            n.R = p;
            if (p.P != _nil)
            {
                if (p == p.P.L) p.P.L = n;
                else p.P.R = n;
            }
            else
                _root = n;
            n.P = p.P;
            p.P = n;
            if (p.L != _nil) p.L.P = p;
        }

        private void InsertNode(Node n)
        {
            TKey k = n.K;
            Node p = _root;
            n.C = Color.Red;
            n.P = n.L = n.R = _nil;
            N++;
            if (p == _nil)
            {
                _root = n;
                n.C = Color.Black;
                return;
            }
            // insert (RED) node into tree
            while (true)
            {
                if (_compare(k, p.K) <= 0)
                {
                    // k <= p.K
                    if (p.L != _nil) p = p.L;
                    else
                    {
                        p.L = n;
                        n.P = p;
                        break;
                    }
                }
                else
                {
                    if (p.R != _nil) p = p.R;
                    else
                    {
                        p.R = n;
                        n.P = p;
                        break;
                    }
                }
            }
        fixtree:
            if (n.P.C == Color.Red)
            {
                // red cannot have red child
                Node u = p == p.P.L ? p.P.R : p.P.L;
                if (u != _nil && u.C == Color.Red)
                {
                    p.C = u.C = Color.Black;
                    n = p.P;
                    if ((p = n.P) != _nil)
                    {
                        n.C = Color.Red;
                        goto fixtree;
                    }
                }
                else
                {
                    if (n == p.R && p == p.P.L)
                    {
                        RotateLeft(p);
                        p = n;
                        n = n.L;
                    }
                    else if (n == p.L && p == p.P.R)
                    {
                        RotateRight(p);
                        p = n;
                        n = n.R;
                    }
                    p.C = Color.Black;
                    p.P.C = Color.Red;
                    if (n == p.L && p == p.P.L)
                        RotateRight(p.P);
                    else if (n == p.R && p == p.P.R)
                        RotateLeft(p.P);
                }
            }
        }

        public Node Insert(TKey k)
        {
            var n = new Node { K = k };
            InsertNode(n);
            return n;
        }

        private bool CheckNode(Node n, out int nblack)
        {
            if (n == _nil)
            {
                nblack = 0;
                return true;
            }
            nblack = 0;
            if (n.R != _nil && n.R.P != n) return false;
            if (n.R != _nil && _compare(n.R.K, n.K) < 0) return false;
            if (n.L != _nil && n.L.P != n) return false;
            if (n.L != _nil && _compare(n.L.K, n.K) > 0) return false;
            if (n.C == Color.Red)
            {
                if (n.R != _nil && n.R.C == Color.Red) return false;
                if (n.L != _nil && n.L.C == Color.Red) return false;
            }
            if (!(CheckNode(n.R, out int nbl) && CheckNode(n.L, out int nbr))) return false;
            if (nbl != nbr) return false;
            nblack = nbl + (n.C == Color.Black ? 1 : 0);
            return true;
        }

        /// <summary>nlopt_rb_tree_check: whether the tree is a valid red-black tree.</summary>
        public bool Check()
        {
            if (_nil.C != Color.Black) return false;
            if (_nil.P != _nil || _nil.R != _nil || _nil.L != _nil) return false;
            if (_root == _nil) return true;
            if (_root.C != Color.Black) return false;
            return CheckNode(_root, out _);
        }

        public Node Find(TKey k)
        {
            Node p = _root;
            while (p != _nil)
            {
                int comp = _compare(k, p.K);
                if (comp == 0) return p;
                p = comp <= 0 ? p.L : p.R;
            }
            return null;
        }

        // find greatest point in subtree p that is <= k
        private Node FindLe(Node p, TKey k)
        {
            while (p != _nil)
            {
                if (_compare(p.K, k) <= 0)
                {
                    // p.K <= k
                    Node r = FindLe(p.R, k);
                    return r ?? p;
                }
                p = p.L; // p.K > k
            }
            return null; // k < everything in subtree
        }

        /// <summary>The greatest key in the tree that is &lt;= k.</summary>
        public Node FindLe(TKey k) => FindLe(_root, k);

        // find greatest point in subtree p that is < k
        private Node FindLt(Node p, TKey k)
        {
            while (p != _nil)
            {
                if (_compare(p.K, k) < 0)
                {
                    // p.K < k
                    Node r = FindLt(p.R, k);
                    return r ?? p;
                }
                p = p.L; // p.K >= k
            }
            return null; // k <= everything in subtree
        }

        /// <summary>The greatest key in the tree that is &lt; k.</summary>
        public Node FindLt(TKey k) => FindLt(_root, k);

        // find least point in subtree p that is > k
        private Node FindGt(Node p, TKey k)
        {
            while (p != _nil)
            {
                if (_compare(p.K, k) > 0)
                {
                    // p.K > k
                    Node l = FindGt(p.L, k);
                    return l ?? p;
                }
                p = p.R; // p.K <= k
            }
            return null; // k >= everything in subtree
        }

        /// <summary>The least key in the tree that is &gt; k.</summary>
        public Node FindGt(TKey k) => FindGt(_root, k);

        public Node Min()
        {
            Node n = _root;
            while (n != _nil && n.L != _nil) n = n.L;
            return n == _nil ? null : n;
        }

        public Node Max()
        {
            Node n = _root;
            while (n != _nil && n.R != _nil) n = n.R;
            return n == _nil ? null : n;
        }

        public Node Succ(Node n)
        {
            if (n == null) return null;
            if (n.R == _nil)
            {
                Node prev;
                do
                {
                    prev = n;
                    n = n.P;
                } while (prev == n.R && n != _nil);
                return n == _nil ? null : n;
            }
            n = n.R;
            while (n.L != _nil) n = n.L;
            return n;
        }

        public Node Pred(Node n)
        {
            if (n == null) return null;
            if (n.L == _nil)
            {
                Node prev;
                do
                {
                    prev = n;
                    n = n.P;
                } while (prev == n.L && n != _nil);
                return n == _nil ? null : n;
            }
            n = n.L;
            while (n.R != _nil) n = n.R;
            return n;
        }

        /// <summary>
        /// Removes a node. The node taken out of the tree may be a different one from
        /// <paramref name="n"/>, holding n's key; that node is returned, as in the C.
        /// </summary>
        public Node Remove(Node n)
        {
            TKey k = n.K;
            Node m, mp;
            if (n.L != _nil && n.R != _nil)
            {
                Node lmax = n.L;
                while (lmax.R != _nil) lmax = lmax.R;
                n.K = lmax.K;
                n = lmax;
            }
            m = n.L != _nil ? n.L : n.R;
            if (n.P != _nil)
            {
                if (n.P.R == n) n.P.R = m;
                else n.P.L = m;
            }
            else
                _root = m;
            mp = n.P;
            if (m != _nil) m.P = mp;
            if (n.C == Color.Black)
            {
                if (m.C == Color.Red)
                    m.C = Color.Black;
                else
                {
                deleteblack:
                    if (mp != _nil)
                    {
                        Node s = m == mp.L ? mp.R : mp.L;
                        if (s.C == Color.Red)
                        {
                            mp.C = Color.Red;
                            s.C = Color.Black;
                            if (m == mp.L) RotateLeft(mp);
                            else RotateRight(mp);
                            s = m == mp.L ? mp.R : mp.L;
                        }
                        if (mp.C == Color.Black && s.C == Color.Black && s.L.C == Color.Black && s.R.C == Color.Black)
                        {
                            if (s != _nil) s.C = Color.Red;
                            m = mp;
                            mp = m.P;
                            goto deleteblack;
                        }
                        else if (mp.C == Color.Red && s.C == Color.Black && s.L.C == Color.Black && s.R.C == Color.Black)
                        {
                            if (s != _nil) s.C = Color.Red;
                            mp.C = Color.Black;
                        }
                        else
                        {
                            if (m == mp.L && s.C == Color.Black && s.L.C == Color.Red && s.R.C == Color.Black)
                            {
                                s.C = Color.Red;
                                s.L.C = Color.Black;
                                RotateRight(s);
                                s = m == mp.L ? mp.R : mp.L;
                            }
                            else if (m == mp.R && s.C == Color.Black && s.R.C == Color.Red && s.L.C == Color.Black)
                            {
                                s.C = Color.Red;
                                s.R.C = Color.Black;
                                RotateLeft(s);
                                s = m == mp.L ? mp.R : mp.L;
                            }
                            s.C = mp.C;
                            mp.C = Color.Black;
                            if (m == mp.L)
                            {
                                s.R.C = Color.Black;
                                RotateLeft(mp);
                            }
                            else
                            {
                                s.L.C = Color.Black;
                                RotateRight(mp);
                            }
                        }
                    }
                }
            }
            N--;
            n.K = k; // n may have changed during remove
            return n; // the node that was deleted may be different from initial n
        }

        /// <summary>Puts a node whose key has changed back in order.</summary>
        public Node Resort(Node n)
        {
            n = Remove(n);
            InsertNode(n);
            return n;
        }
    }
}
