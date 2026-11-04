import React, { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Activity, ArrowDownToLine, ArrowRight, BarChart3, Boxes, Check, CircleAlert, Clock3, LayoutDashboard, Leaf, LogOut, Package, Plus, Search, ShoppingBag, Trash2, Truck, Users, X } from 'lucide-react';
import './style.css';

type Page<T> = { items: T[]; totalCount: number };
type Customer = { id: string; name: string; email: string; phone?: string; createdAtUtc: string };
type Product = { id: string; sku: string; name: string; description?: string; price: number; stockOnHand: number; availableStock: number; isAvailable: boolean };
type Order = { id: string; customerId: string; status: number; total: number; createdAtUtc: string; items: { productId: string; quantity: number; unitPrice: number; lineTotal: number }[] };
type History = { status: number; changedAtUtc: string };
type Transaction = { id: string; productId: string; type: number; quantity: number; reference?: string; createdAtUtc: string };
type Analytics = { orderCount: number; sales: number; averageOrderValue: number; cancelledOrders: number; popularProducts: { productId: string; sku: string; name: string; quantitySold: number }[] };
type View = 'Overview' | 'Orders' | 'Customers' | 'Products' | 'Inventory' | 'Analytics';
const statusNames = ['Pending', 'Confirmed', 'Processing', 'Shipped', 'Delivered', 'Cancelled'];
const statusNext: Record<number, number[]> = { 0: [1, 5], 1: [2, 5], 2: [3, 5], 3: [4], 4: [], 5: [] };
const money = (value: number) => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value);
const shortId = (id: string) => id.slice(0, 8).toUpperCase();
const date = (value: string) => new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' }).format(new Date(value));

function App() {
  const [token, setToken] = useState(localStorage.getItem('rf-token') || '');
  const [role, setRole] = useState(localStorage.getItem('rf-role') || '');
  const [email, setEmail] = useState(localStorage.getItem('rf-email') || '');
  const [view, setView] = useState<View>('Overview');
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [analytics, setAnalytics] = useState<Analytics | null>(null);
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [modal, setModal] = useState<'customer' | 'product' | 'order' | 'receipt' | null>(null);
  const [editing, setEditing] = useState<Customer | Product | null>(null);
  const [detail, setDetail] = useState<Order | null>(null);
  const [history, setHistory] = useState<History[]>([]);
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [stockProduct, setStockProduct] = useState('');
  const [dateFrom, setDateFrom] = useState(() => new Date(Date.now() - 30 * 86400000).toISOString().slice(0, 10));
  const [dateTo, setDateTo] = useState(() => new Date().toISOString().slice(0, 10));

  const call = useCallback(async <T,>(path: string, method = 'GET', data?: unknown): Promise<T> => {
    const response = await fetch(`/api/${path}`, { method, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(data ? { 'Content-Type': 'application/json' } : {}) }, body: data ? JSON.stringify(data) : undefined });
    if (!response.ok) {
      if (response.status === 401) { localStorage.removeItem('rf-token'); setToken(''); }
      const payload = await response.json().catch(() => null);
      const message = payload?.detail || payload?.title || (payload?.errors ? Object.values(payload.errors).flat().join(' ') : '') || `Request failed (${response.status})`;
      throw new Error(message);
    }
    return (response.status === 204 ? null : response.json()) as Promise<T>;
  }, [token]);

  const refresh = useCallback(async () => {
    if (!token) return;
    setLoading(true); setError('');
    try {
      const [c, p, o] = await Promise.all([
        call<Page<Customer>>('customers?pageSize=100'),
        call<Page<Product>>('products?pageSize=100'),
        call<Page<Order>>('orders?pageSize=100')
      ]);
      setCustomers(c.items); setProducts(p.items); setOrders(o.items);
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not load business data.'); }
    finally { setLoading(false); }
  }, [call, token]);

  useEffect(() => { void refresh(); }, [refresh]);
  useEffect(() => {
    if (!token || view !== 'Analytics' || role !== 'Admin') return;
    setLoading(true); setError('');
    const until = new Date(`${dateTo}T00:00:00.000Z`);
    until.setUTCDate(until.getUTCDate() + 1);
    void call<Analytics>(`analytics/overview?from=${dateFrom}&to=${until.toISOString()}`).then(setAnalytics).catch(e => setError(e.message)).finally(() => setLoading(false));
  }, [call, token, view, role, dateFrom, dateTo]);
  useEffect(() => {
    if (!stockProduct) { setTransactions([]); return; }
    void call<Page<Transaction>>(`inventory/products/${stockProduct}/transactions?pageSize=100`).then(r => setTransactions(r.items)).catch(e => setError(e.message));
  }, [call, stockProduct]);

  const filteredCustomers = useMemo(() => customers.filter(x => `${x.name} ${x.email} ${x.phone || ''}`.toLowerCase().includes(query.toLowerCase())), [customers, query]);
  const filteredProducts = useMemo(() => products.filter(x => `${x.name} ${x.sku}`.toLowerCase().includes(query.toLowerCase())), [products, query]);
  const recentOrders = [...orders].sort((a, b) => +new Date(b.createdAtUtc) - +new Date(a.createdAtUtc)).slice(0, 6);
  const lowStock = products.filter(x => x.availableStock <= 5 && x.isAvailable);
  const greeting = email ? email.split('@')[0] : 'team';
  const todayLabel = new Intl.DateTimeFormat('en-US', { weekday: 'long' }).format(new Date()).toUpperCase();

  async function login(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = new FormData(event.currentTarget); setError(''); setLoading(true);
    try {
      const result = await call<{ accessToken: string; roles: string[]; email: string }>('auth/login', 'POST', { email: form.get('email'), password: form.get('password') });
      const userRole = result.roles?.[0] || 'Employee';
      localStorage.setItem('rf-token', result.accessToken); localStorage.setItem('rf-role', userRole); localStorage.setItem('rf-email', result.email);
      setToken(result.accessToken); setRole(userRole); setEmail(result.email);
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to sign in.'); }
    finally { setLoading(false); }
  }
  function logout() { localStorage.removeItem('rf-token'); localStorage.removeItem('rf-role'); localStorage.removeItem('rf-email'); setToken(''); setRole(''); setEmail(''); setView('Overview'); }
  function openModal(kind: NonNullable<typeof modal>, item: Customer | Product | null = null) { setEditing(item); setError(''); setModal(kind); }
  async function saveRecord(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = new FormData(event.currentTarget); const isEdit = Boolean(editing);
    try {
      if (modal === 'customer') {
        const body = { name: form.get('name'), email: form.get('email'), phone: form.get('phone') || null };
        await call(`customers${isEdit ? `/${(editing as Customer).id}` : ''}`, isEdit ? 'PUT' : 'POST', body);
      } else if (modal === 'product') {
        if (isEdit) await call(`products/${(editing as Product).id}`, 'PUT', { name: form.get('name'), description: form.get('description') || null, price: Number(form.get('price')), isAvailable: form.get('isAvailable') === 'on' });
        else await call('products', 'POST', { sku: form.get('sku'), name: form.get('name'), description: form.get('description') || null, price: Number(form.get('price')), stock: Number(form.get('stock')) });
      }
      setModal(null); setNotice(`${modal === 'customer' ? 'Customer' : 'Product'} ${isEdit ? 'updated' : 'created'}.`); await refresh();
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not save this record.'); }
  }
  async function removeRecord(kind: 'customers' | 'products', item: Customer | Product) {
    const label = 'name' in item ? item.name : '';
    if (!window.confirm(`Delete ${label}? This action cannot be undone.`)) return;
    try { await call(`${kind}/${item.id}`, 'DELETE'); setNotice(`${kind === 'customers' ? 'Customer' : 'Product'} deleted.`); await refresh(); }
    catch (e) { setError(e instanceof Error ? e.message : 'Delete failed.'); }
  }
  async function createOrder(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = new FormData(event.currentTarget); const items = products.filter(p => Number(form.get(`qty-${p.id}`)) > 0).map(p => ({ productId: p.id, quantity: Number(form.get(`qty-${p.id}`)) }));
    if (!items.length) { setError('Add at least one available product.'); return; }
    try { await call('orders', 'POST', { customerId: form.get('customerId'), items }); setModal(null); setNotice('Order created and stock reserved.'); await refresh(); }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not create order.'); }
  }
  async function showOrder(order: Order) {
    setError(''); setDetail(order);
    try { setHistory(await call<History[]>(`orders/${order.id}/history`)); }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not load order history.'); }
  }
  async function changeStatus(order: Order, next: number) {
    if (!window.confirm(`Move order #${shortId(order.id)} to ${statusNames[next]}?`)) return;
    try { await call(`orders/${order.id}/status`, 'PATCH', { status: next }); setNotice(`Order moved to ${statusNames[next]}.`); setDetail(null); await refresh(); }
    catch (e) { setError(e instanceof Error ? e.message : 'Status update failed.'); }
  }
  async function receiveStock(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = new FormData(event.currentTarget);
    try { await call('inventory/transactions', 'POST', { productId: form.get('productId'), quantity: Number(form.get('quantity')), type: 0, reference: form.get('reference') || null }); setModal(null); setNotice('Stock receipt recorded.'); await refresh(); }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not record receipt.'); }
  }

  if (!token) return <main className="login-page"><section className="login-card"><div className="login-mark"><img src="/logo.jpeg" alt="RalseiFlow logo" /></div><span className="eyebrow">A CLEARER WAY TO TRADE</span><h1>Welcome back.</h1><p className="lead">Orders, stock & business — in one place.</p><form onSubmit={login} className="form-stack"><label>Email address<input autoComplete="username" name="email" type="email" placeholder="you@company.com" required /></label><label>Password<input autoComplete="current-password" name="password" type="password" placeholder="Your password" required /></label>{error && <div className="error"><CircleAlert size={16}/>{error}</div>}<button className="primary wide" disabled={loading}>{loading ? 'Signing in…' : 'Sign in'}<ArrowRight size={17}/></button></form><footer>Independent project · No sponsors</footer></section><aside className="login-aside"><div className="login-aside-art"><div className="orbit orbit-one"/><div className="orbit orbit-two"/><img src="/logo.jpeg" alt="" /></div><p>Good operations are built one thoughtful detail at a time.</p><span>RALSEIFLOW · WORKSPACE</span></aside></main>;

  const nav: { label: View; icon: React.ElementType }[] = [
    { label: 'Overview', icon: LayoutDashboard }, { label: 'Orders', icon: ShoppingBag }, { label: 'Customers', icon: Users }, { label: 'Products', icon: Package }, { label: 'Inventory', icon: Boxes }, ...(role === 'Admin' ? [{ label: 'Analytics' as View, icon: BarChart3 }] : [])
  ];
  const titles: Record<View, string> = { Overview: 'Your business, at a glance.', Orders: 'Orders', Customers: 'Customers', Products: 'Products', Inventory: 'Inventory', Analytics: 'Sales, in context.' };
  return <div className="app-shell"><aside className="sidebar"><a className="brand" href="#overview" onClick={e => {e.preventDefault();setView('Overview')}}><img src="/logo.jpeg" alt=""/><span>ralsei<span>flow</span></span></a><div className="nav-caption">WORKSPACE</div><nav>{nav.map(({label, icon: Icon}) => <button key={label} className={view === label ? 'nav-link active' : 'nav-link'} onClick={() => {setView(label);setQuery('');setError('')}}><Icon size={18}/><span>{label}</span>{label === 'Orders' && <small>{orders.filter(x => x.status < 3).length}</small>}</button>)}</nav><div className="sidebar-note"><Leaf size={16}/><span>Small steps.<br/>Sound operations.</span></div><div className="profile"><div className="avatar">{email.slice(0,1).toUpperCase()}</div><div className="profile-copy"><b>{email.split('@')[0]}</b><span>{role}</span></div><button title="Sign out" aria-label="Sign out" className="icon-button" onClick={logout}><LogOut size={17}/></button></div><footer>Independent project · No sponsors</footer></aside>
  <main className="content"><header className="topbar"><div><span className="eyebrow">{todayLabel} · BUSINESS DESK</span><h1>{titles[view]}</h1><p>{view === 'Overview' ? `A good day to keep things moving, ${greeting}.` : `Manage your ${view.toLowerCase()} with a little more clarity.`}</p></div><div className="top-actions"><span className="live-dot">Connected</span><div className="user-chip"><span className="avatar small">{email.slice(0,1).toUpperCase()}</span><span>{role}</span></div></div></header>
    {error && <div className="toast error"><CircleAlert size={16}/>{error}<button aria-label="Dismiss" onClick={() => setError('')}><X size={16}/></button></div>}{notice && <div className="toast success"><Check size={16}/>{notice}<button aria-label="Dismiss" onClick={() => setNotice('')}><X size={16}/></button></div>}
    {loading && <div className="loading-line"><span/></div>}
    {view === 'Overview' && <><div className="welcome-band"><div><span>THE DAILY EDIT</span><h2>Keep the good work<br/>in motion.</h2><p>Today’s snapshot of your orders, people and stock.</p></div><div className="welcome-orbit"><div/><Leaf size={46}/><span>01 / 04</span></div></div><section className="metric-grid"><Metric label="Open orders" value={orders.filter(x => x.status < 4 && x.status !== 5).length.toString().padStart(2,'0')} sub="Moving through your workflow" icon={<ShoppingBag/>} /><Metric label="Customers" value={customers.length.toString().padStart(2,'0')} sub="People who keep you going" icon={<Users/>} /><Metric label="Available items" value={products.length.toString().padStart(2,'0')} sub={`${lowStock.length} need a closer look`} icon={<Package/>} /><Metric label="Order value" value={money(orders.filter(x => x.status !== 5).reduce((s,x)=>s+x.total,0))} sub="Across recorded orders" icon={<Activity/>} /></section><div className="overview-grid"><section className="panel recent-panel"><div className="panel-heading"><div><span className="eyebrow">RECENT ACTIVITY</span><h2>Orders on the move</h2></div><button className="text-button" onClick={() => setView('Orders')}>All orders <ArrowRight size={15}/></button></div><OrderTable orders={recentOrders} customers={customers} onOpen={showOrder}/></section><section className="panel stock-panel"><div className="panel-heading"><div><span className="eyebrow">STOCK WATCH</span><h2>Low on the shelf</h2></div><Boxes size={19}/></div>{lowStock.length ? lowStock.slice(0,5).map(p=><div className="stock-row" key={p.id}><div className="stock-symbol"><Package size={17}/></div><div><b>{p.name}</b><small>{p.sku}</small></div><span className="stock-count">{p.availableStock}<small>left</small></span></div>) : <div className="calm-state"><Check size={18}/>Everything looks comfortably stocked.</div>}<button className="text-button stock-action" onClick={() => setView('Inventory')}>Review inventory <ArrowRight size={15}/></button></section></div></>}
    {view === 'Customers' && <section className="panel data-panel"><div className="panel-heading"><div><span className="eyebrow">YOUR PEOPLE</span><h2>Customer directory <small>{customers.length}</small></h2></div><div className="toolbar"><Search size={17}/><input value={query} onChange={e=>setQuery(e.target.value)} placeholder="Find a customer"/><button className="primary" onClick={()=>openModal('customer')}><Plus size={16}/> Add customer</button></div></div><div className="table-wrap"><table><thead><tr><th>Customer</th><th>Email</th><th>Phone</th><th>Added</th><th/></tr></thead><tbody>{filteredCustomers.map(c=><tr key={c.id}><td><span className="table-avatar">{c.name.slice(0,1).toUpperCase()}</span><b>{c.name}</b></td><td>{c.email}</td><td>{c.phone || '—'}</td><td>{date(c.createdAtUtc)}</td><td className="row-actions"><button onClick={()=>openModal('customer',c)}>Edit</button>{role==='Admin'&&<button className="danger-link" onClick={()=>void removeRecord('customers',c)}><Trash2 size={15}/></button>}</td></tr>)}</tbody></table>{!filteredCustomers.length&&<Empty label="No customers match this search."/>}</div></section>}
    {view === 'Products' && <section className="panel data-panel"><div className="panel-heading"><div><span className="eyebrow">THE CATALOGUE</span><h2>Products & pricing <small>{products.length}</small></h2></div><div className="toolbar"><Search size={17}/><input value={query} onChange={e=>setQuery(e.target.value)} placeholder="Find a product"/>{role==='Admin'&&<button className="primary" onClick={()=>openModal('product')}><Plus size={16}/> Add product</button>}</div></div><div className="table-wrap"><table><thead><tr><th>Item</th><th>SKU</th><th>Price</th><th>On hand</th><th>Available</th><th>Status</th><th/></tr></thead><tbody>{filteredProducts.map(p=><tr key={p.id}><td><b>{p.name}</b></td><td className="mono">{p.sku}</td><td>{money(p.price)}</td><td>{p.stockOnHand}</td><td>{p.availableStock}</td><td><span className={p.isAvailable?'pill':'pill muted'}>{p.isAvailable?'Active':'Paused'}</span></td><td className="row-actions">{role==='Admin'&&<><button onClick={()=>openModal('product',p)}>Edit</button><button className="danger-link" onClick={()=>void removeRecord('products',p)}><Trash2 size={15}/></button></>}</td></tr>)}</tbody></table>{!filteredProducts.length&&<Empty label="No products match this search."/>}</div></section>}
    {view === 'Orders' && <section className="panel data-panel"><div className="panel-heading"><div><span className="eyebrow">FROM CART TO CUSTOMER</span><h2>Order book <small>{orders.length}</small></h2></div><div className="toolbar"><Search size={17}/><input value={query} onChange={e=>setQuery(e.target.value)} placeholder="Search order or status"/><button className="primary" onClick={()=>openModal('order')}><Plus size={16}/> New order</button></div></div><OrderTable orders={orders.filter(o=>`${shortId(o.id)} ${statusNames[o.status]}`.toLowerCase().includes(query.toLowerCase()))} customers={customers} onOpen={showOrder} detailed/></section>}
    {view === 'Inventory' && <><section className="inventory-banner"><div><span className="eyebrow">A HEALTHY BUFFER</span><h2>Stock with confidence.</h2><p>Receipts and availability, all accounted for.</p></div><button className="primary" onClick={()=>openModal('receipt')}><ArrowDownToLine size={17}/> Record a receipt</button></section><section className="panel data-panel"><div className="panel-heading"><div><span className="eyebrow">CURRENT STOCK</span><h2>Inventory ledger</h2></div><div className="toolbar"><Search size={17}/><input value={query} onChange={e=>setQuery(e.target.value)} placeholder="Find an item"/></div></div><div className="table-wrap"><table><thead><tr><th>Product</th><th>SKU</th><th>On hand</th><th>Reserved</th><th>Available</th><th>History</th></tr></thead><tbody>{filteredProducts.map(p=><tr key={p.id}><td><b>{p.name}</b></td><td className="mono">{p.sku}</td><td>{p.stockOnHand}</td><td>{p.stockOnHand-p.availableStock}</td><td><span className={p.availableStock<=5?'stock-low':''}>{p.availableStock}</span></td><td><button className="text-button" onClick={()=>setStockProduct(p.id)}>View transactions <ArrowRight size={14}/></button></td></tr>)}</tbody></table>{!filteredProducts.length&&<Empty label="No inventory to show yet."/>}</div></section>{stockProduct&&<section className="panel transaction-panel"><div className="panel-heading"><div><span className="eyebrow">MOVEMENT LOG</span><h2>{products.find(p=>p.id===stockProduct)?.name} transactions</h2></div><button className="icon-button" aria-label="Close transaction history" onClick={()=>setStockProduct('')}><X size={18}/></button></div>{transactions.length?transactions.map(t=><div className="transaction-row" key={t.id}><div className="transaction-icon"><ArrowDownToLine size={16}/></div><div><b>{['Receipt','Reservation','Release','Adjustment'][t.type] || 'Movement'}</b><small>{t.reference||'Stock movement'} · {date(t.createdAtUtc)}</small></div><strong className={t.type===1?'negative':''}>{t.type===1?'−':'+'}{t.quantity}</strong></div>):<Empty label="No transactions recorded for this product."/>}</section>}</>}
    {view === 'Analytics' && role === 'Admin' && <><div className="analytics-filters"><span>Sales period</span><label>From<input type="date" value={dateFrom} max={dateTo} onChange={e=>setDateFrom(e.target.value)}/></label><ArrowRight size={16}/><label>To<input type="date" value={dateTo} min={dateFrom} onChange={e=>setDateTo(e.target.value)}/></label></div>{analytics&&<><section className="metric-grid analytics-metrics"><Metric label="Orders in period" value={String(analytics.orderCount)} sub="Orders recorded in range" icon={<ShoppingBag/>}/><Metric label="Gross sales" value={money(analytics.sales)} sub="Excludes cancelled orders" icon={<BarChart3/>}/><Metric label="Average order" value={money(analytics.averageOrderValue)} sub="Per fulfilled or open order" icon={<Activity/>}/><Metric label="Cancelled" value={String(analytics.cancelledOrders)} sub="Orders cancelled in range" icon={<X/>}/></section><section className="panel chart-panel"><div className="panel-heading"><div><span className="eyebrow">PRODUCT MOMENTUM</span><h2>Most ordered items</h2></div><span className="chart-period">{date(dateFrom)} — {date(dateTo)}</span></div>{analytics.popularProducts.length?analytics.popularProducts.map((p,i)=><div className="bar-row" key={p.productId}><span className="bar-index">0{i+1}</span><b>{p.name}<small>{p.sku}</small></b><div className="bar-track"><span style={{width:`${Math.max(6,p.quantitySold/Math.max(...analytics.popularProducts.map(x=>x.quantitySold))*100)}%`}}/></div><strong>{p.quantitySold}<small>units</small></strong></div>):<Empty label="No sales recorded for this period."/>}</section></>}</>}
    <footer className="content-footer"><span>RALSEIFLOW</span><span>Independent project · No sponsors</span><a href="/swagger" target="_blank" rel="noreferrer">API documentation <ArrowRight size={13}/></a></footer>
  </main>
  {modal && <div className="modal-scrim" onMouseDown={e=>{if(e.target===e.currentTarget)setModal(null)}}><section className="modal"><div className="modal-head"><div><span className="eyebrow">{modal==='order'?'NEW WORK':modal==='receipt'?'STOCK IN':editing?'MAKE IT CURRENT':'ADD TO YOUR BOOKS'}</span><h2>{modal==='order'?'Create an order':modal==='receipt'?'Record stock receipt':modal==='customer'?editing?'Edit customer':'New customer':editing?'Edit product':'New product'}</h2></div><button className="icon-button" onClick={()=>setModal(null)} aria-label="Close"><X size={18}/></button></div>
    {modal==='customer'&&<form className="form-stack" onSubmit={saveRecord}><label>Full name<input name="name" required maxLength={120} defaultValue={(editing as Customer|null)?.name}/></label><label>Email<input name="email" type="email" required maxLength={254} defaultValue={(editing as Customer|null)?.email}/></label><label>Phone (optional)<input name="phone" type="tel" maxLength={40} defaultValue={(editing as Customer|null)?.phone||''}/></label>{error&&<div className="error">{error}</div>}<div className="modal-actions"><button type="button" className="secondary" onClick={()=>setModal(null)}>Cancel</button><button className="primary">Save customer</button></div></form>}
    {modal==='product'&&<form className="form-stack" onSubmit={saveRecord}>{!editing&&<label>SKU<input name="sku" required maxLength={64}/></label>}<label>Product name<input name="name" required maxLength={160} defaultValue={(editing as Product|null)?.name}/></label><label>Description<input name="description" maxLength={500} defaultValue={(editing as Product|null)?.description||''}/></label><div className="form-row"><label>Unit price<input name="price" type="number" min="0" step="0.01" required defaultValue={(editing as Product|null)?.price}/></label>{!editing?<label>Opening stock<input name="stock" type="number" min="0" step="1" required defaultValue="0"/></label>:<label className="checkbox-label"><input name="isAvailable" type="checkbox" defaultChecked={(editing as Product|null)?.isAvailable}/> Available for sale</label>}</div>{error&&<div className="error">{error}</div>}<div className="modal-actions"><button type="button" className="secondary" onClick={()=>setModal(null)}>Cancel</button><button className="primary">Save product</button></div></form>}
    {modal==='order'&&<form className="form-stack order-form" onSubmit={createOrder}><label>Customer<select name="customerId" required defaultValue=""><option value="" disabled>Select customer</option>{customers.map(c=><option value={c.id} key={c.id}>{c.name} · {c.email}</option>)}</select></label><div className="order-products"><div className="order-product-head"><span>Product</span><span>Available</span><span>Qty</span></div>{products.filter(p=>p.isAvailable).map(p=><label className="order-product" key={p.id}><span><b>{p.name}</b><small>{money(p.price)} · {p.sku}</small></span><span>{p.availableStock}</span><input aria-label={`Quantity for ${p.name}`} name={`qty-${p.id}`} type="number" min="0" max={p.availableStock} defaultValue="0"/></label>)}</div>{error&&<div className="error">{error}</div>}<div className="modal-actions"><button type="button" className="secondary" onClick={()=>setModal(null)}>Cancel</button><button className="primary" disabled={!customers.length}>Create order</button></div></form>}
    {modal==='receipt'&&<form className="form-stack" onSubmit={receiveStock}><label>Product<select name="productId" required defaultValue=""><option value="" disabled>Select product</option>{products.map(p=><option value={p.id} key={p.id}>{p.name} · {p.sku}</option>)}</select></label><label>Quantity received<input name="quantity" type="number" min="1" step="1" required/></label><label>Reference (optional)<input name="reference" maxLength={160} placeholder="Purchase order, delivery note…"/></label>{error&&<div className="error">{error}</div>}<div className="modal-actions"><button type="button" className="secondary" onClick={()=>setModal(null)}>Cancel</button><button className="primary"><ArrowDownToLine size={16}/> Record receipt</button></div></form>}
  </section></div>}
  {detail&&<div className="modal-scrim" onMouseDown={e=>{if(e.target===e.currentTarget)setDetail(null)}}><section className="modal detail-modal"><div className="modal-head"><div><span className="eyebrow">ORDER · {shortId(detail.id)}</span><h2>Order details</h2></div><button className="icon-button" onClick={()=>setDetail(null)} aria-label="Close"><X size={18}/></button></div><div className="order-detail-top"><span className={`status-badge s${detail.status}`}>{statusNames[detail.status]}</span><b>{money(detail.total)}</b><small>{customers.find(c=>c.id===detail.customerId)?.name||'Customer'} · {date(detail.createdAtUtc)}</small></div><div className="detail-lines">{detail.items.map((line,i)=><div key={i}><span>{products.find(p=>p.id===line.productId)?.name||`Item ${shortId(line.productId)}`}<small>{line.quantity} × {money(line.unitPrice)}</small></span><b>{money(line.lineTotal)}</b></div>)}</div><h3 className="history-title"><Clock3 size={16}/> Order history</h3><div className="timeline">{history.map((h,i)=><div className="timeline-item" key={`${h.status}-${i}`}><i/><div><b>{statusNames[h.status]}</b><small>{new Date(h.changedAtUtc).toLocaleString()}</small></div></div>)}</div><div className="detail-actions">{(statusNext[detail.status]||[]).map(next=><button className={next===5?'secondary danger-button':'primary'} key={next} onClick={()=>void changeStatus(detail,next)}>{next===3?<Truck size={16}/>:<Check size={16}/>} Mark {statusNames[next].toLowerCase()}</button>)}</div></section></div>}
  </div>;
}

function Metric({label,value,sub,icon}:{label:string;value:string;sub:string;icon:React.ReactNode}) { return <article className="metric-card"><div className="metric-top"><span>{label}</span><i>{icon}</i></div><strong>{value}</strong><small>{sub}</small></article>; }
function Empty({label}:{label:string}) { return <div className="empty-state"><Package size={22}/><span>{label}</span></div>; }
function OrderTable({orders,customers,onOpen,detailed=false}:{orders:Order[];customers:Customer[];onOpen:(order:Order)=>void;detailed?:boolean}) { return <div className="table-wrap"><table><thead><tr><th>Order</th><th>Customer</th><th>Date</th><th>Status</th><th>Total</th>{detailed&&<th>Lines</th>}<th/></tr></thead><tbody>{orders.map(o=><tr key={o.id} onClick={()=>onOpen(o)} className="click-row"><td className="mono">#{shortId(o.id)}</td><td>{customers.find(c=>c.id===o.customerId)?.name||'Customer'}</td><td>{date(o.createdAtUtc)}</td><td><span className={`status-badge s${o.status}`}>{statusNames[o.status]}</span></td><td>{money(o.total)}</td>{detailed&&<td>{o.items.length} items</td>}<td><button className="row-open" aria-label={`Open order ${shortId(o.id)}`}><ArrowRight size={16}/></button></td></tr>)}</tbody></table>{!orders.length&&<Empty label="No orders yet. New work will appear here."/>}</div>; }

createRoot(document.getElementById('root')!).render(<App/>);
