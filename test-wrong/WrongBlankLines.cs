using System;
using System.Collections.Generic;

namespace TestWrong
{

    // 1. Blank line after opening brace (should be removed)
    public class BlankLineViolations
    {

        // 2. Blank line after opening brace (should be removed)
        private readonly string _name;
        private readonly int _count;


        // 3. Two consecutive blank lines (should collapse to one)
        public BlankLineViolations(string name, int count)
        {

            // 4. Blank line after opening brace (should be removed)
            _name = name;
            _count = count;

            // 5. Blank line before closing brace (should be removed)

        }

        public string Name => _name;



        // 6. Three consecutive blank lines (should collapse to one)
        public int Count => _count;

        #region Public Methods
        // 7. Missing blank line after #region (should add one)

        public void DoWork()
        {
            Console.WriteLine(_name);
        }

        public void DoMoreWork()
        {

            // 8. Blank line after opening brace (should be removed)
            for (int i = 0; i < _count; i++)
            {

                // 9. Blank line after opening brace (should be removed)
                Console.WriteLine($"Item {i}");

                // 10. Blank line before closing brace (should be removed)

            }

            // 11. Blank line before closing brace (should be removed)

        }
        // 12. Missing blank line before #endregion (should add one)
        #endregion

        #region Private Helpers

        private string Format()
        {
            return $"{_name}: {_count}";
        }

        private void Log(string message)
        {

            // 13. Blank line after opening brace
            Console.Error.WriteLine(message);

            // 14. Blank line before closing brace

        }

        #endregion

        #region Nested Types
        // 15. Missing blank after #region

        public class Inner
        {

            // 16. Blank after brace
            public int Value { get; set; }

            // 17. Blank before brace

        }
        // 18. Missing blank before #endregion
        #endregion
    }
}
