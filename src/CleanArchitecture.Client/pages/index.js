import Head from 'next/head';
import Image from 'next/image';
import { useContextualRouting } from '../views/routes.views';
import styles from '../assets/styles/Home.module.css';
import Link from 'next/link';
import { useClient } from '../components';
import { useState } from 'react';
import { findContextualRoute } from '../views/routes';
import { getPath } from '../utils';

export default function Home(props) {

  const client = useClient();
  const { constructLink } = useContextualRouting();
  const [count, setCount] = useState(0);

  return (
    <div className={styles.container}>
      <Head>
        <title>Create Next App</title>
        <meta name="description" content="Generated by create next app" />
        <link rel="icon" href="/favicon.ico" />
      </Head>
      <Link href="/terms"><a>Home</a></Link>
      <br />
      <Link {...constructLink({ pathname: '/account/signin', query: { name: undefined } })}><a>Sign In</a></Link>
      <br />
      <Link {...constructLink('/account/signup')}><a>Sign Up</a></Link>
      <br />
      <Link {...constructLink('/account/password/change')}><a>Change Password</a></Link>
      <br />
      <Link {...constructLink('/account/verify')}><a>Verify Account</a></Link>
      <br />
      <br />
      <Link {...constructLink('/account/password/reset')}><a>Reset Password</a></Link>
      <br />
      <br />

      <main className={styles.main}>
        <h1 className={styles.title} style={{ marginTop: "-100px" }}>
          Welcome to <a onClick={() => setCount(count + 1)}>Home.js! {count}</a>
        </h1>

        <p className={styles.description}>
          Get started by editing{' '}
          <code className={styles.code}>pages/index.js</code>
        </p>
        <div>{JSON.stringify(client.user)}</div>
        <div className={styles.grid}>
          <a href="https://nextjs.org/docs" className={styles.card}>
            <h2>Documentation &rarr;</h2>
            <p>Find in-depth information about Next.js features and API.</p>
          </a>

          <a href="https://nextjs.org/learn" className={styles.card}>
            <h2>Learn &rarr;</h2>
            <p>Learn about Next.js in an interactive course with quizzes!</p>
          </a>

          <a
            href="https://github.com/vercel/next.js/tree/canary/examples"
            className={styles.card}
          >
            <h2>Examples &rarr;</h2>
            <p>Discover and deploy boilerplate example Next.js projects.</p>
          </a>

          <a
            href="https://vercel.com/new?utm_source=create-next-app&utm_medium=default-template&utm_campaign=create-next-app"
            className={styles.card}
          >
            <h2>Deploy &rarr;</h2>
            <p>
              Instantly deploy your Next.js site to a public URL with Vercel.
            </p>
          </a>
        </div>
      </main>

      <footer className={styles.footer}>
        <a
          href="https://vercel.com?utm_source=create-next-app&utm_medium=default-template&utm_campaign=create-next-app"
          target="_blank"
          rel="noopener noreferrer"
        >
          Powered by{' '}
          <span className={styles.logo}>
            <Image src="/vercel.svg" alt="Vercel Logo" width={72} height={16} />
          </span>
        </a>
      </footer>
    </div>
  )
}

