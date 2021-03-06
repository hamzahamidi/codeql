CodeQL for Python
=================

Experiment and learn how to write effective and efficient queries for CodeQL databases generated from Python codebases.

.. toctree::
   :hidden:

   introduce-libraries-python
   functions
   statements-expressions
   pointsto-type-infer
   control-flow
   taint-tracking

-  `Basic Python query <https://lgtm.com/help/lgtm/console/ql-python-basic-example>`__ : Learn to write and run a simple CodeQL query using LGTM.

-  :doc:`CodeQL library for Python <introduce-libraries-python>`: When you need to analyze a Python program, you can make use of the large collection of classes in the CodeQL library for Python.

-  :doc:`Functions in Python <functions>`: You can use syntactic classes from the standard CodeQL library to find Python functions and identify calls to them.

-  :doc:`Expressions and statements in Python <statements-expressions>`: You can use syntactic classes from the CodeQL library to explore how Python expressions and statements are used in a codebase.

-  :doc:`Analyzing control flow in Python <control-flow>`: You can write CodeQL queries to explore the control-flow graph of a Python program, for example, to discover unreachable code or mutually exclusive blocks of code.

-  :doc:`Pointer analysis and type inference in Python <pointsto-type-infer>`: At runtime, each Python expression has a value with an associated type. You can learn how an expression behaves at runtime by using type-inference classes from the standard CodeQL library.

-  :doc:`Analyzing data flow and tracking tainted data in Python <taint-tracking>`: You can use CodeQL to track the flow of data through a Python program. Tracking user-controlled, or tainted, data is a key technique for security researchers.

Further reading
---------------

-  For examples of how to query common Python elements, see the `Python cookbook <https://help.semmle.com/wiki/display/CBPython>`__.
-  For the queries used in LGTM, display a `Python query <https://lgtm.com/search?q=language%3APython&t=rules>`__ and click **Open in query console** to see the code used to find alerts.
-  For more information about the library for JavaScript see the `CodeQL library for Python <https://help.semmle.com/qldoc/python/>`__.
