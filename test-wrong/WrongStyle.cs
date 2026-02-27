using System;
using System.Collections.Generic;
using System.Linq;

namespace TestWrong
{
    // 1. Inheritance: colon on SAME line (should be new line)
    public class MyService 
        : IDisposable, ICloneable
    {
        private readonly string _name;
        private readonly int _timeout;
        private readonly bool _enabled;

        // 2. Parameters: 3+ params on single line (should be multi-line)
        public MyService(
            string name,
            int timeout,
            bool enabled
        )
        {
            _name = name;
            _timeout = timeout;
            _enabled = enabled;
        }

        // 3. Arrow on same line (should be new line)
        public string Name 
            => _name;

        public int Timeout 
            => _timeout;

        // 4. Arrow on same line for method
        public string GetDisplayName() 
            => $"{_name} (timeout={_timeout})";

        // 5. Ternary: single-line but long (should be multi-line with aligned ? :)
        public string GetStatus() 
            => _enabled
                ? $"Service '{_name}' is active with timeout {_timeout}ms"
                : $"Service '{_name}' is disabled";

        // 6. Method chaining: 3+ calls on single line (should be multi-line)
        public List<string> GetFilteredItems(IEnumerable<string> items)
        {
            return items
                .Where(x => x.StartsWith(_name))
                .OrderBy(x => x)
                .Select(x => x.ToUpper())
                .ToList();
        }

        // 7. Object initializer with trailing comma (should have no trailing comma)
        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["name"] = _name,
                ["timeout"] = _timeout,
                ["enabled"] = _enabled
            };
        }

        // 8. Another single-line param list that should wrap
        public void Configure(
            string host,
            int port,
            string protocol,
            bool useSsl
        )
        {
            Console.WriteLine($"{protocol}://{host}:{port} ssl={useSsl}");
        }

        // 9. Inheritance on same line for nested class
        public class Builder 
            : IDisposable
        {
            private string _name = "";
            private int _timeout = 30;

            public Builder WithName(string name) 
                => throw new NotImplementedException();

            // 10. Another long ternary
            public string Validate()
            {
                var result = string.IsNullOrEmpty(_name)
                    ? "Name is required and must not be empty"
                    : "OK";
                return result;
            }

            public void Dispose() { }
        }

        // 11. Struct with same-line inheritance
        public struct Options 
            : IEquatable<Options>
        {
            public string Name { get; init; }
            public int Timeout { get; init; }

            public bool Equals(Options other) 
                => Name == other.Name && Timeout == other.Timeout;
        }

        public void Dispose() { }

        public object Clone() 
            => throw new NotImplementedException();
    }
}
