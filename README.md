# Whirlwind

Whirlwind is a compiled, modern, and multipurpose language designed with intentionality.
It is strongly-typed, versatile, expressive, concurrent, and relatively easy to learn.
It boasts numerous new and old features and is designed to represent the needs of any software developer.

***Language and Source Code Copyright &copy; Jordan Gaines 2019-2020***

*Note: Most of this will be moved to a formal website at some point.*

*Note: All here is subject to change.*

***NOTE: This language is still in development!***

## Table of Contents

- [Goals](#goals)
- [Notable Features](#features)
- [Feature Demos](#demos)
- [Examples](#examples)
- [Contributing](#contributing)
- [Author's Note](#note)

## <a name="goals"/> Goals

Whirlwind has several main goals all of which can be enscapsulated in the idea of speed.

- Speed of writing.
- Speed of thinking.
- Speed of compiling.
- Speed of running.

Whirlwind tries to achieve all of these goals, and admittedly falls short in some areas.  When using more high level constructs,
there is often a trade off between ease of thought and writing and speed of compilation and execution.  When designing this
language, I aimed for an "85% solution" which effectively means close enough but not perfect: each construct fulfills the majority of our speed goals but not all at once.  In essence, instead of trying to make each and every construct perfect, I provide a variety of constructs and approaches each of which is suited to one or more of the goals, this design allowing you to choose what you find most important.

## <a name="features"/> Notable Features

- Versatile Type System
- Intelligent Memory Model (no GC)
- Baked-In Concurrency
- Builtin Collections (arrays, lists, dictionaries)
- Pattern Matching
- Baked-In Vectors (and SIMD)
- Interface Binding
- Flexible Generics
- First Class Functions
- Powerful, Logical Type Inference
- Functional Programming Features
- Simple, Flexible Package System

*Note: This is not a complete list.  Also, many of the more unique features have additional information about them below.*

## <a name="demos"/> Feature Demos

***TODO: Insert some demos and explanations!***

## <a name="examples"/> Examples

***TODO: Add some more examples!***

Fibonacci:

    import { println } from io::std;

    func fib() func()(int) {
        let a = 0, b = 1;

        func f() int {
            yield a;

            a, b = b, a + b;
        }

        return f;
    }

    func main() {
        let f = fib();

        // prints first 10 fibonacci numbers
        for i = 0; i < 10; i++ {
            println(f());
        }

        f = fib();
        f(); // 0
    }

Radix Sort:

    import { println } from io::std;

    func radix_sort(list: [uint]) [uint] {
        let mx = list.max();

        for it = 0; 10 ~^ it < mx; it++ {
            let buckets = [null as [int] for _ in 1..10];

            for item in list {
                buckets[item ~/ (10 ~^ it) % 10].push(item);
            }             

            list = list.flatten().to_list();
        }

        return list;
    }

    func main() {
        let list = [9, 4, 7, 8, 2, 3, 9, 0, 0, 1];

        list = radix_sort(list);

        println(list); // [0, 0, 1, 2, 3, 4, 7, 8, 9, 9]
    }

Linked List:

    import { println } from io::std;

    type LLNode {
        value: int,
        next: own& LLNode
    }

    func ll_range(val: int) own &LLNode {
        if val == 0 {
            return make LLNode{value=val};
        }

        return make LLNode{value=val, next=ll_range(val - 1)};
    }

    func main() {
        let ll = ll_range(10);

        let p = ll;
        for i = 0; i < 11; i++ {
            println(p.value);
            p = p.next;
        }
    }

## <a name="contributing"/> Contributing

***TODO: Insert links***

Information about the compiler and all of its components can be found [here](insert link here).

If you would like to contribute to this compiler or this language, please review the information above
and join our Discord and other social media to get up to speed on the language in its current state.

- [Discord](insert link here)
- [Slack](insert link here)
- [Reddit](insert link here

**Please read the full reference provided above before contributing the
repository.**

## <a name="note"/> Author's Note

This compiler is the realization of my vision as a programmer. I have been obsessed with languages
and language design pretty much since the first day I picked up programming. I hope that this language
can be as amazing to you as it is to me. I am always happy to have people who want to make
suggestions and contributions to help make this language as great as it can be.

By the time it is finished, this language will likely be the culminations of thousands of hours of work
and I sincerely believe it will be worth it.
