# DelegatePool
## 概要
本ライブラリ「DelegatePool」はDelegate生成時のアロケーションを抑制します。  
Delegateオブジェクトを事前にPoolしておき、必要なときに再利用することでゼロアロケーションなDelegate生成を実現します。  
またラムダ式によって発生するキャッシュインスタンスもPoolすることが可能です。  

## 動作確認環境
|  環境  |  バージョン  |
| ---- | ---- |
| Unity | 2021.3.15f1, 2022.2.0f1 |
| .Net | 4.x, Standard 2.1 |

## 性能
### エディタ上の計測コード
[テストコード](packages/Tests/Runtime/DelegatePoolPerformanceTest.cs) 

#### 結果
|  実行処理  |  処理時間  |
| ---- | ---- |
| Instance_Legacy | 0.2419 ms |
| Instance_Pool | 1.81385 ms |
| Instance_ConcurrentPool | 1.9628 ms |
| Instance_ThreadStaticPool | 1.7624 ms |
| Lambda_Legacy | 0.4348 ms |
| Lambda_Pool | 2.8968 ms |
| Lambda_ConcurrentPool | 3.52965 ms |
| Lambda_ThreadStaticPool | 2.8054 ms |

エディタ環境ではPoolのほうが高コストですが、後述のIL2CPP環境では異なる結果になります。

### ビルド後の計測コード
```.cs
private readonly ref struct Measure
{
    private readonly string _label;
    private readonly StringBuilder _builder;
    private readonly float _time;

    public Measure(string label, StringBuilder builder)
    {
        _label = label;
        _builder = builder;
        _time = (Time.realtimeSinceStartup * 1000);
    }

    public void Dispose()
    {
        _builder.AppendLine($"{_label}: {(Time.realtimeSinceStartup * 1000) - _time} ms");
    }
}

 :

var log = new StringBuilder();
var t = new TestFunctions.Test();
using (new Measure("Instance_Legacy", log))
{
    for (int i = 0; i < 5000; ++i)
    {
        Instance_Legacy(t);
    }
}

using (new Measure("Instance_Pool", log))
{
    for (int i = 0; i < 5000; ++i)
    {
        Instance_Pool(t);
    }
}

 :

public void Instance_Legacy(TestFunctions.Test t)
{
    Func<int> f = t.Return1;
    f();
}

public void Instance_Pool(TestFunctions.Test t)
{
    using (DelegatePool<Func<int>>.Get(t.Return1, out var f))
    {
        f();
    }
}
```
#### 結果
|  実行処理  |  Mono  |  IL2CPP  |
| ---- | ---- | ---- |
| Instance_Legacy | 1.375793 ms | 1.275879 ms |
| Instance_Pool | 1.507324 ms | 0.2495117 ms |
| Instance_ConcurrentPool | 2.047913 ms | 1.229492 ms |
| Instance_ThreadStaticPool | 1.435272 ms | 0.300293 ms |
| Lambda_Legacy | 1.321472 ms | 1.114258 ms |
| Lambda_Pool | 2.068634 ms | 0.4941406 ms |
| Lambda_ConcurrentPool | 3.389587 ms | 2.715332 ms |
| Lambda_ThreadStaticPool | 1.974792 ms | 0.6586914 ms |

IL2CPP環境で5倍程度の性能改善が見られます。  
また、アロケーションを抑制できるためメモリパフォーマンスもエコです。  

## インストール方法
### 依存パッケージをインストール
以下のパッケージをインストールする。  

- [ILPostProcessorCommon v2.2.0](https://github.com/Katsuya100/ILPostProcessorCommon/tree/v2.2.0)
- [BoxingPool v1.3.0](https://github.com/Katsuya100/BoxingPool/tree/v1.3.0)
- [MemoizationForUnity v1.4.2](https://github.com/Katsuya100/MemoizationForUnity/tree/v1.4.2)

### DelegatePoolのインストール
1. [Window > Package Manager]を開く。
2. [+ > Add package from git url...]をクリックする。
3. `https://github.com/Katsuya100/DelegatePool.git?path=packages`と入力し[Add]をクリックする。

#### うまくいかない場合
上記方法は、gitがインストールされていない環境ではうまく動作しない場合があります。
[Releases](https://github.com/Katsuya100/DelegatePool/releases)から該当のバージョンの`com.katuusagi.delegatepool.tgz`をダウンロードし
[Package Manager > + > Add package from tarball...]を使ってインストールしてください。

#### それでもうまくいかない場合
[Releases](https://github.com/Katsuya100/DelegatePool/releases)から該当のバージョンの`Katuusagi.DelegatePool.unitypackage`をダウンロードし
[Assets > Import Package > Custom Package]からプロジェクトにインポートしてください。

## 使い方
### 通常の使用法
以下の記法でDelegatePoolを使用できます。  
usingステートメントを使わない場合解放漏れによってパフォーマンスが低下する場合があります。  
```.cs
public static void Hoge()
{
}

:

using(DelegatePool<Action>.Get(Hoge, out var a))
{
    a();
}
```

詳しい人が一見すると`Hoge`がインスタンス化されているように見えますが、実際はそうはなりません。  
この実装は以下のように展開されます。  
```.cs
Action a;
DelegatePool<Action>.GetHandler classOnly = DelegatePool<Action>.GetClassOnly<DelegatePoolTest>(null, (nint)(delegate*<void>)(&Hoge), null, out a);
try
{
    a();
}
finally
{
    classOnly.Dispose();
}
```

`ReadOnlyHandler`型にキャストすれば、Handlerをメンバで保持することが可能です。  

```.cs
private DelegatePool<Action>.ReadOnlyHandler _handle;

:

private void OnDestroy()
{
    _handle.Dispose();
}

:

_handle = DelegatePool<Action>.Get(Hoge, out var o);
```

### ラムダ式のインスタンスをPoolする
以下の記法でラムダ式のインスタンスをPoolできます。  
```.cs
int v = 1;
using(DelegatePool<Action>.Get(() => Debug.Log(v), out var a))
{
    a();
}
```
この実装は以下のように展開されます。
```.cs
IReferenceHandler h = default(IReferenceHandler);
try
{
    CountPool<<>c__DisplayClass13_0>.Get(ref h, out var result);
    result.v = 1;
    Action a;
    DelegatePool<Action>.GetHandler classOnly = DelegatePool<Action>.GetClassOnly(result, (nint)__ldftn(<>c__DisplayClass13_0.<A>b__0), h, out a);
    try
    {
        a();
    }
    finally
    {
        classOnly.Dispose();
    }
}
finally
{
    CountPool<<>c__DisplayClass13_0>.Return(h);
}
```
ラムダ式がCountPoolによってPoolされています。  
CountPoolは参照カウントを用いて返却管理するObjectPoolです。  
DelegatePoolが参照しているため、ラムダ式の返却管理が可能です。  

### マルチスレッドに対応したい場合
マルチスレッド環境で使用したい場合は  
`ConcurrentDelegatePool`を使用してください。  
```.cs
using(ConcurrentDelegatePool<Action>.Get(Hoge, out var a))
{
    a();
}
```
Concurrentシリーズは他のDelegatePoolと異なり、固有のPoolを持っています。  
これにより、マルチスレッド環境でも使用することが可能です。  
しかし、DelegatePoolに比べて性能面での課題があります。  
具体的にはReturn時にアロケーションが発生します。  
後のアップデートで改善していく予定です。  

#### ThreadStaticなプール
`ThreadStaticDelegatePool`を使用することで  
パフォーマンスを落とさずにマルチスレッド対応が可能です。  
```.cs
using(ThreadStaticDelegatePool<Action>.Get(Hoge, out var a))
{
    a();
}
```
Thread毎に異なるプールを用いるため、Concurrentシリーズに比べてメモリ消費量が多くなる可能性があります。
また、異なるスレッドでDisoseしないように注意してください。  
返却は正常に完了しますが取得したプールと異なるプールに返却されてしまいます。  

## 高速な理由
Delegateやラムダ式はプログラマの目に見えない形でインスタンス化されます。  
そのため、従来はPoolすることが不可能でした。
DelegatePoolではILPostProcessorを用い目に見えないインスタンスをPoolするよう対応しています。

`MethodImpl`属性で`AggressiveInline`を設定しているため、ビルド時のインライン展開による最適化も期待できます。  
以上のテクニックによりゼロアロケーションでのDelegateインスタンス化に成功しています。  