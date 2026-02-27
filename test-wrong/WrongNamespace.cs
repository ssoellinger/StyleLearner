using System;
using System.Collections.Generic;

namespace TestWrong
{
    public class SimpleClass
    {
        private readonly string _value;

        public SimpleClass(string value)
        {
            _value = value;
        }

        public string GetValue()
            => _value;

        public class Inner
        {
            public int Count { get; set; }
        }
    }
}
